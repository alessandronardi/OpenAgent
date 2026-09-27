using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OpenAgent.Data.Hardware;

public static class HardwareDetector
{
    private enum LOGICAL_PROCESSOR_RELATIONSHIP
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3,
        RelationGroup = 4,
        RelationAll = 0xffff
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(
        LOGICAL_PROCESSOR_RELATIONSHIP relationshipType,
        IntPtr buffer,
        ref int returnedLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Rileva le specifiche hardware della macchina in modo ultrarapido e affidabile.
    /// </summary>
    public static HardwareSpecs DetectSpecs(string? projectRootDir = null)
    {
        string cpuName = DetectCpuName();
        int logicalCores = Environment.ProcessorCount;
        int physicalCores = DetectPhysicalCores(logicalCores);
        var (totalRamBytes, availRamBytes) = DetectMemory();
        string? gpuName = DetectGpuName();
        bool hasVulkan = CheckVulkanSupport(projectRootDir);

        double totalRamGb = totalRamBytes / (1024.0 * 1024.0 * 1024.0);
        double availRamGb = availRamBytes / (1024.0 * 1024.0 * 1024.0);

        // Calcolo del Tier hardware
        HardwareTier tier;
        if (totalRamGb <= 8.5 || physicalCores <= 4 || availRamGb < 3.2)
        {
            tier = HardwareTier.Entry;
        }
        else if (totalRamGb >= 16.0 && physicalCores >= 8 && availRamGb >= 6.0)
        {
            tier = HardwareTier.HighPerformance;
        }
        else
        {
            tier = HardwareTier.Balanced;
        }

        return new HardwareSpecs
        {
            CpuName = cpuName,
            PhysicalCores = physicalCores,
            LogicalCores = logicalCores,
            TotalPhysicalMemoryBytes = totalRamBytes,
            AvailablePhysicalMemoryBytes = availRamBytes,
            GpuName = gpuName,
            HasVulkanSupport = hasVulkan,
            Tier = tier
        };
    }

    private static string DetectCpuName()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var val = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null);
                if (val is string str && !string.IsNullOrWhiteSpace(str))
                {
                    return str.Trim();
                }
            }
            catch
            {
                // Fallback
            }
        }

        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Processore x64";
    }

    private static int DetectPhysicalCores(int logicalCores)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Math.Max(1, logicalCores / 2);
        }

        try
        {
            int length = 0;
            GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, IntPtr.Zero, ref length);
            if (length <= 0)
            {
                return Math.Max(1, logicalCores / 2);
            }

            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, buffer, ref length))
                {
                    int count = 0;
                    int offset = 0;
                    while (offset < length)
                    {
                        int relation = Marshal.ReadInt32(buffer, offset);
                        int size = Marshal.ReadInt32(buffer, offset + 4);
                        if (relation == (int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                        {
                            count++;
                        }
                        if (size <= 0) break;
                        offset += size;
                    }
                    if (count > 0) return count;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // Ignore and fallback
        }

        return Math.Max(1, logicalCores / 2);
    }

    private static (ulong TotalBytes, ulong AvailBytes) DetectMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var mem = new MEMORYSTATUSEX();
                mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref mem))
                {
                    return (mem.ullTotalPhys, mem.ullAvailPhys);
                }
            }
            catch
            {
                // Fallback
            }
        }

        // Stima di fallback tramite GC memory info
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            if (gcInfo.TotalAvailableMemoryBytes > 0)
            {
                ulong total = (ulong)gcInfo.TotalAvailableMemoryBytes;
                return (total, total / 2);
            }
        }
        catch
        {
            // Ignore
        }

        // Fallback conservativo 8 GB totali, 4 GB liberi
        return (8UL * 1024 * 1024 * 1024, 4UL * 1024 * 1024 * 1024);
    }

    private static string? DetectGpuName()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            const string basePath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var baseKey = Registry.LocalMachine.OpenSubKey(basePath);
            if (baseKey != null)
            {
                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    if (!subName.StartsWith("00", StringComparison.OrdinalIgnoreCase)) continue;
                    using var subKey = baseKey.OpenSubKey(subName);
                    var desc = subKey?.GetValue("DriverDesc")?.ToString();
                    if (!string.IsNullOrWhiteSpace(desc) &&
                        !desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) &&
                        !desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                    {
                        return desc.Trim();
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    private static bool CheckVulkanSupport(string? projectRootDir)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            // Verifica driver Vulkan nel sistema Windows
            string systemVulkan = Path.Combine(Environment.SystemDirectory, "vulkan-1.dll");
            if (!File.Exists(systemVulkan)) return false;

            // Se è fornita la root del progetto, verifica anche che esista ggml-vulkan.dll in runtime
            if (!string.IsNullOrWhiteSpace(projectRootDir))
            {
                string runtimeVulkan = Path.Combine(projectRootDir, "runtime", "llama", "ggml-vulkan.dll");
                if (!File.Exists(runtimeVulkan)) return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
