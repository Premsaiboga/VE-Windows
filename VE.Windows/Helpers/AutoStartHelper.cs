using Microsoft.Win32;

namespace VE.Windows.Helpers;

/// <summary>
/// Launch at Windows startup via registry.
/// Equivalent to macOS LaunchAtLogin.
/// </summary>
public static class AutoStartHelper
{
    private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "VE AI Desktop";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                if (key == null) return;

                if (value)
                {
                    var exePath = Environment.ProcessPath
                        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (exePath != null)
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("AutoStart", $"Failed to set auto-start: {ex.Message}");
            }
        }
    }
}
