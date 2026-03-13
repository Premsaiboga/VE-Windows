using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using VE.Windows.Helpers;

namespace VE.Windows.Managers;

/// <summary>
/// Screenshot capture and active window detection.
/// Equivalent to macOS XPC helper (ActiveWindowScraperHelper, CaretVisualDetector).
/// </summary>
public sealed class ScreenCaptureManager
{
    public static ScreenCaptureManager Instance { get; } = new();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private ScreenCaptureManager() { }

    public string GetActiveWindowTitle()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, 256);
            return sb.ToString();
        }
        catch { return ""; }
    }

    public string GetActiveAppName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            GetWindowThreadProcessId(hwnd, out uint processId);
            var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch { return ""; }
    }

    public byte[]? CaptureActiveWindow()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            if (!GetWindowRect(hwnd, out RECT rect)) return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return null;

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ScreenCapture", $"Capture failed: {ex.Message}");
            return null;
        }
    }

    public byte[]? CaptureScreen()
    {
        try
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (screen == null) return null;

            var bounds = screen.Bounds;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ScreenCapture", $"Screen capture failed: {ex.Message}");
            return null;
        }
    }
}
