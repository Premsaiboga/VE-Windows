using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace VE.Windows.Views.Notch;

/// <summary>
/// Separate error capsule window that appears below the notch.
/// Auto-dismisses after 3 seconds with fade animation.
/// </summary>
public partial class ErrorCapsuleWindow : Window
{
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static ErrorCapsuleWindow? _instance;

    public ErrorCapsuleWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show an error message in a capsule below the notch. Auto-dismisses after 3 seconds.
    /// </summary>
    public static void ShowError(string message)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Close existing capsule
                _instance?.Close();

                var capsule = new ErrorCapsuleWindow();
                _instance = capsule;
                capsule.ErrorText.Text = message;

                // Position below the notch (center of screen, ~42px from top)
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                capsule.Left = (screenWidth - capsule.Width) / 2;
                capsule.Top = 42;

                capsule.Show();

                // Make it a tool window that doesn't steal focus
                var hwnd = new WindowInteropHelper(capsule).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    exStyle |= WS_EX_TOOLWINDOW;
                    exStyle |= WS_EX_NOACTIVATE;
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }

                // Fade in
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                capsule.BeginAnimation(OpacityProperty, fadeIn);

                // Auto-dismiss after 3 seconds
                Task.Delay(3000).ContinueWith(_ =>
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        if (_instance == capsule)
                        {
                            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                            fadeOut.Completed += (s, e) =>
                            {
                                capsule.Close();
                                if (_instance == capsule) _instance = null;
                            };
                            capsule.BeginAnimation(OpacityProperty, fadeOut);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                Helpers.FileLogger.Instance.Error("ErrorCapsule", $"Failed to show: {ex.Message}");
            }
        });
    }
}
