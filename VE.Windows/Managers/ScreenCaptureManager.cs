using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using VE.Windows.Helpers;

namespace VE.Windows.Managers;

/// <summary>
/// Screenshot capture and active window detection.
/// Matches macOS: captures full screen, resizes to max 1660px, encodes as JPEG 85%.
/// </summary>
public sealed class ScreenCaptureManager
{
    public static ScreenCaptureManager Instance { get; } = new();

    private const int MaxScreenshotWidth = 1660;
    private const long JpegQuality = 85;

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

    /// <summary>
    /// Capture full screen as JPEG with resizing (matches macOS behavior).
    /// macOS captures full screen, resizes to max 1660px width, encodes as JPEG 85%.
    /// </summary>
    public byte[]? CaptureActiveWindow()
    {
        try
        {
            // Capture full primary screen (matches macOS CGWindowListCreateImage)
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (screen == null) return null;

            var bounds = screen.Bounds;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);

            // Resize if wider than max (matches macOS 1660px max)
            Bitmap finalBitmap = bitmap;
            bool needsDispose = false;

            if (bitmap.Width > MaxScreenshotWidth)
            {
                var ratio = (double)MaxScreenshotWidth / bitmap.Width;
                var newHeight = (int)(bitmap.Height * ratio);
                finalBitmap = new Bitmap(MaxScreenshotWidth, newHeight);
                needsDispose = true;
                using var g = Graphics.FromImage(finalBitmap);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bitmap, 0, 0, MaxScreenshotWidth, newHeight);
            }

            // Encode as JPEG 85% quality (matches macOS)
            using var ms = new MemoryStream();
            var jpegEncoder = GetJpegEncoder();
            if (jpegEncoder != null)
            {
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
                finalBitmap.Save(ms, jpegEncoder, encoderParams);
            }
            else
            {
                finalBitmap.Save(ms, ImageFormat.Jpeg);
            }

            if (needsDispose) finalBitmap.Dispose();

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
        return CaptureActiveWindow(); // Now same behavior
    }

    private static ImageCodecInfo? GetJpegEncoder()
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == ImageFormat.Jpeg.Guid)
                return codec;
        }
        return null;
    }
}
