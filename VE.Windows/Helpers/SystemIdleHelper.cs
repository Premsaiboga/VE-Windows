using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VE.Windows.Helpers;

public sealed class SystemIdleHelper : IDisposable
{
    public static SystemIdleHelper Instance { get; } = new();

    public event EventHandler? OnSystemSleep;
    public event EventHandler? OnSystemResume;

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    // Prevent system sleep during recording
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    private SystemIdleHelper()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                FileLogger.Instance.Info("SystemIdle", "System entering sleep");
                OnSystemSleep?.Invoke(this, EventArgs.Empty);
                break;
            case PowerModes.Resume:
                FileLogger.Instance.Info("SystemIdle", "System waking from sleep");
                OnSystemResume?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref info))
        {
            var idleMs = (uint)Environment.TickCount - info.dwTime;
            return TimeSpan.FromMilliseconds(idleMs);
        }
        return TimeSpan.Zero;
    }

    public void PreventSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
    }

    public void AllowSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS);
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        AllowSleep();
    }
}
