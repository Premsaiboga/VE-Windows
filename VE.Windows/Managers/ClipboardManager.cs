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
    private const byte VK_LCONTROL = 0xA2;
    private const byte VK_RCONTROL = 0xA3;
    private const byte VK_MENU = 0x12;     // Alt
    private const byte VK_LMENU = 0xA4;
    private const byte VK_RMENU = 0xA5;
    private const byte VK_SHIFT = 0x10;
    private const byte VK_LSHIFT = 0xA0;
    private const byte VK_RSHIFT = 0xA1;
    private const byte VK_LWIN = 0x5B;
    private const byte VK_RWIN = 0x5C;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

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
    /// Matches macOS PasteHelper: releases all held modifier keys before pasting.
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

            // Release all physically held modifier keys before pasting (matches macOS PasteHelper)
            // This prevents conflicts when prediction Ctrl key or dictation Shift key is still down
            ReleaseAllModifierKeys();

            // Wait for modifier keys to fully release (matches macOS 100ms delay)
            Thread.Sleep(100);

            // Simulate Ctrl+V using keybd_event
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30); // Wait for Ctrl to register (matches macOS)
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50); // Wait before releasing V (matches macOS)
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(10); // Wait before releasing Ctrl (matches macOS)
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            FileLogger.Instance.Debug("Clipboard", $"Pasted {text.Length} chars");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Clipboard", $"Paste failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Release all modifier keys that are currently held down.
    /// Matches macOS PasteHelper which releases Option, Shift, Control before Cmd+V.
    /// </summary>
    private void ReleaseAllModifierKeys()
    {
        byte[] modifiers = { VK_CONTROL, VK_LCONTROL, VK_RCONTROL,
                             VK_MENU, VK_LMENU, VK_RMENU,
                             VK_SHIFT, VK_LSHIFT, VK_RSHIFT,
                             VK_LWIN, VK_RWIN };

        foreach (var key in modifiers)
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                FileLogger.Instance.Debug("Clipboard", $"Released held key: 0x{key:X2}");
            }
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
