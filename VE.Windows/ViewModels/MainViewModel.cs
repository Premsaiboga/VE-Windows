using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace VE.Windows.ViewModels;

public enum NotchState { Closed, Open }

/// <summary>
/// Main view model for the floating notch window.
/// Equivalent to macOS VEAIViewModel.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private NotchState _notchState = NotchState.Closed;
    private double _notchWidth = 220;
    private double _notchHeight = 36;
    private double _openWidth = 380;
    private double _openHeight = 500;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? NotchOpened;
    public event EventHandler? NotchClosed;

    public NotchState NotchState
    {
        get => _notchState;
        set { _notchState = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsOpen)); }
    }

    public bool IsOpen => _notchState == NotchState.Open;

    public double NotchWidth
    {
        get => _notchState == NotchState.Open ? _openWidth : _notchWidth;
        set { _notchWidth = value; OnPropertyChanged(); }
    }

    public double NotchHeight
    {
        get => _notchState == NotchState.Open ? _openHeight : _notchHeight;
        set { _notchHeight = value; OnPropertyChanged(); }
    }

    public double ClosedWidth
    {
        get => _notchWidth;
        set { _notchWidth = value; OnPropertyChanged(); }
    }

    public double ClosedHeight
    {
        get => _notchHeight;
        set { _notchHeight = value; OnPropertyChanged(); }
    }

    public double OpenWidth
    {
        get => _openWidth;
        set { _openWidth = value; OnPropertyChanged(); }
    }

    public double OpenHeight
    {
        get => _openHeight;
        set { _openHeight = value; OnPropertyChanged(); }
    }

    public void Open()
    {
        if (_notchState == NotchState.Open) return;
        NotchState = NotchState.Open;
        OnPropertyChanged(nameof(NotchWidth));
        OnPropertyChanged(nameof(NotchHeight));
        NotchOpened?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        if (_notchState == NotchState.Closed) return;
        NotchState = NotchState.Closed;
        OnPropertyChanged(nameof(NotchWidth));
        OnPropertyChanged(nameof(NotchHeight));
        NotchClosed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle()
    {
        if (_notchState == NotchState.Open) Close();
        else Open();
    }

    /// <summary>
    /// Calculate window position centered at top of the target screen.
    /// Tracks per-display position: if the user drags to a different monitor,
    /// remembers position for that monitor. Falls back to primary if saved display is gone.
    /// </summary>
    public Point GetWindowPosition()
    {
        var targetScreen = GetTargetScreen();
        var x = targetScreen.Left + (targetScreen.Width - NotchWidth) / 2;
        return new Point(x, targetScreen.Top);
    }

    /// <summary>
    /// Save the current display as the user's preferred notch display.
    /// Call this after the user drags the notch to a different monitor.
    /// </summary>
    public void SaveDisplayPreference(Point windowPosition)
    {
        try
        {
            // Find which screen contains this position
            foreach (var screen in GetAllScreens())
            {
                if (windowPosition.X >= screen.Left && windowPosition.X < screen.Left + screen.Width)
                {
                    var key = $"{screen.Width}x{screen.Height}@{screen.Left},{screen.Top}";
                    Models.SettingsManager.Instance.Set("PreferredNotchDisplay", key);
                    Helpers.FileLogger.Instance.Debug("MainVM", $"Saved display preference: {key}");
                    return;
                }
            }
        }
        catch { }
    }

    private Rect GetTargetScreen()
    {
        try
        {
            var savedKey = Models.SettingsManager.Instance.Get<string?>("PreferredNotchDisplay", null);
            if (savedKey != null)
            {
                // Try to find the saved display
                foreach (var screen in GetAllScreens())
                {
                    var key = $"{screen.Width}x{screen.Height}@{screen.Left},{screen.Top}";
                    if (key == savedKey)
                    {
                        return screen;
                    }
                }
                // Saved display not found — fall through to primary
                Helpers.FileLogger.Instance.Debug("MainVM", $"Saved display '{savedKey}' not found, using primary");
            }
        }
        catch { }

        // Default: primary screen
        return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private static IEnumerable<Rect> GetAllScreens()
    {
        var screens = new List<Rect>();
        var dpiScale = GetDpiScale();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                screens.Add(new Rect(
                    mi.rcWork.Left / dpiScale,
                    mi.rcWork.Top / dpiScale,
                    (mi.rcWork.Right - mi.rcWork.Left) / dpiScale,
                    (mi.rcWork.Bottom - mi.rcWork.Top) / dpiScale));
            }
            return true;
        }, IntPtr.Zero);

        return screens;
    }

    private static double GetDpiScale()
    {
        try
        {
            var source = PresentationSource.FromVisual(Application.Current.MainWindow);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
