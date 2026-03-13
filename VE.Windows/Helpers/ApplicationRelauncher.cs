using System.Diagnostics;
using System.Windows;

namespace VE.Windows.Helpers;

public static class ApplicationRelauncher
{
    public static void Restart()
    {
        var exePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName;

        if (exePath != null)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            });
        }

        Application.Current.Shutdown();
    }
}
