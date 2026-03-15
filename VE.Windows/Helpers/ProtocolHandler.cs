using Microsoft.Win32;
using VE.Windows.Managers;

namespace VE.Windows.Helpers;

/// <summary>
/// Register and handle ve:// protocol for OAuth callbacks.
/// Equivalent to macOS URL scheme handling in AppDelegate+URLHandling.
/// </summary>
public static class ProtocolHandler
{
    private const string Protocol = "ve";
    private const string ProtocolDescription = "VE AI Desktop Protocol";

    public static void RegisterProtocol()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath == null) return;

            // Check if already registered with current path to avoid unnecessary writes
            if (IsRegisteredWithPath(exePath)) return;

            using var key = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\Classes\{Protocol}");
            key?.SetValue("", $"URL:{ProtocolDescription}");
            key?.SetValue("URL Protocol", "");

            using var iconKey = key?.CreateSubKey("DefaultIcon");
            iconKey?.SetValue("", $"\"{exePath}\",0");

            using var commandKey = key?.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");

            FileLogger.Instance.Info("ProtocolHandler", $"ve:// protocol registered: {exePath}");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ProtocolHandler", $"Registration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if ve:// protocol is already registered pointing to the current exe path.
    /// </summary>
    private static bool IsRegisteredWithPath(string exePath)
    {
        try
        {
            using var commandKey = Registry.CurrentUser.OpenSubKey($@"SOFTWARE\Classes\{Protocol}\shell\open\command", false);
            var currentValue = commandKey?.GetValue("") as string;
            if (currentValue == null) return false;

            var expectedValue = $"\"{exePath}\" \"%1\"";
            return string.Equals(currentValue, expectedValue, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void UnregisterProtocol()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"SOFTWARE\Classes\{Protocol}", false);
        }
        catch { }
    }

    public static async Task HandleUri(string uri)
    {
        if (!uri.StartsWith("ve://")) return;

        FileLogger.Instance.Info("ProtocolHandler", $"Handling URI: {uri}");

        try
        {
            var parsedUri = new Uri(uri);

            if (parsedUri.Host == "callback" || parsedUri.Host == "oauth")
            {
                var query = System.Web.HttpUtility.ParseQueryString(parsedUri.Query);
                var sessionId = query["sessionId"];

                if (!string.IsNullOrEmpty(sessionId))
                {
                    await AuthManager.Instance.HandleOAuthCallback(sessionId);
                }
                else
                {
                    FileLogger.Instance.Warning("ProtocolHandler", "No sessionId in callback URI");
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ProtocolHandler", $"URI handling failed: {ex.Message}");
        }
    }
}
