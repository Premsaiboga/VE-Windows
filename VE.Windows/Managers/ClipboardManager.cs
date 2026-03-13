using System.Runtime.InteropServices;
using System.Windows;
using VE.Windows.Helpers;

namespace VE.Windows.Managers;

/// <summary>
/// Clipboard operations and text pasting via simulated Ctrl+V.
/// Equivalent to macOS AppHelper + PasteHelper.
/// </summary>
public sealed class ClipboardManager
{
    public static ClipboardManager Instance { get; } = new();

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private ClipboardManager() { }

    public string? ReadClipboard()
    {
        string? text = null;
        RunOnSTAThread(() =>
        {
            try { text = Clipboard.GetText(); }
            catch { }
        });
        return text;
    }

    public void WriteClipboard(string text)
    {
        RunOnSTAThread(() =>
        {
            try { Clipboard.SetText(text); }
            catch { }
        });
    }

    /// <summary>
    /// Sets clipboard text and simulates Ctrl+V to paste into active window.
    /// </summary>
    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            // Set clipboard on UI thread
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(text);
            });

            // Small delay for clipboard to update
            Thread.Sleep(50);

            // Simulate Ctrl+V using keybd_event (more reliable than SendInput for modifier combos)
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            FileLogger.Instance.Debug("Clipboard", $"Pasted {text.Length} chars");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Clipboard", $"Paste failed: {ex.Message}");
        }
    }

    private static void RunOnSTAThread(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
        }
        else
        {
            var thread = new Thread(() => action());
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
    }
}
