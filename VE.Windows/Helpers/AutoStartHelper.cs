using Microsoft.Win32;

namespace VE.Windows.Helpers;

/// <summary>
/// Launch at Windows startup via registry.
/// Equivalent to macOS LaunchAtLogin.
/// Verifies registry path matches current exe on startup to handle app moved/updated.
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
                    var exePath = GetCurrentExePath();
                    if (exePath != null)
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                        FileLogger.Instance.Info("AutoStart", $"Enabled: {exePath}");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    FileLogger.Instance.Info("AutoStart", "Disabled");
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("AutoStart", $"Failed to set auto-start: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Verify on startup that registry path matches current exe path.
    /// Handles edge cases: app moved to different path, exe updated in place.
    /// </summary>
    public static void VerifyRegistryPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            if (key == null) return;

            var registeredValue = key.GetValue(AppName) as string;
            if (registeredValue == null) return; // Not registered, nothing to fix

            var currentExePath = GetCurrentExePath();
            if (currentExePath == null) return;

            var expectedValue = $"\"{currentExePath}\"";

            if (!string.Equals(registeredValue, expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                // Registry points to old path — update to current exe path
                FileLogger.Instance.Warning("AutoStart",
                    $"Registry path mismatch. Was: {registeredValue}, Now: {expectedValue}");
                key.SetValue(AppName, expectedValue);
                FileLogger.Instance.Info("AutoStart", "Registry path updated to current exe");
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("AutoStart", $"Registry verify failed: {ex.Message}");
        }
    }

    private static string? GetCurrentExePath()
    {
        return Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
    }
}
