using System;
using System.IO;
using Microsoft.Win32;

namespace OpenAgent.App.Native;

public static class WindowsAutostartManager
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "OpenAgent";

    public static bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetAutostart(bool enable, bool startMinimized = false)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return false;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = Path.Combine(AppContext.BaseDirectory, "OpenAgent.exe");
                }

                string command = startMinimized
                    ? $"\"{exePath}\" --minimized"
                    : $"\"{exePath}\"";

                key.SetValue(AppName, command);
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
