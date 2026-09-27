using System;

namespace OpenAgent.Data.Hardware;

public enum HardwareTier
{
    Entry,           // <= 4 core fisici o <= 8 GB RAM (laptop base, mini PC, risorse limitate)
    Balanced,        // 4-8 core, 8-16 GB RAM (desktop/laptop mainstream)
    HighPerformance  // >= 8 core, >= 16 GB RAM (workstation, gaming PC ad alte prestazioni)
}

public sealed class HardwareSpecs
{
    public string CpuName { get; init; } = "CPU Sconosciuta";
    public int PhysicalCores { get; init; } = 4;
    public int LogicalCores { get; init; } = 4;
    public ulong TotalPhysicalMemoryBytes { get; init; }
    public ulong AvailablePhysicalMemoryBytes { get; init; }
    public double TotalRamGb => Math.Round(TotalPhysicalMemoryBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public double AvailableRamGb => Math.Round(AvailablePhysicalMemoryBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public bool HasVulkanSupport { get; init; }
    public string? GpuName { get; init; }
    public HardwareTier Tier { get; init; } = HardwareTier.Balanced;

    public string Summary =>
        $"{CpuName} ({PhysicalCores}C/{LogicalCores}T) | RAM: {AvailableRamGb:F1}/{TotalRamGb:F1} GB | GPU: {GpuName ?? (HasVulkanSupport ? "Vulkan Generic" : "CPU Only")}";
}
