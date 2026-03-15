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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_MENU = 0x12;     // Alt
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_V = 0x56;

    // F-keys that might be held down as trigger keys
    private const ushort VK_F1 = 0x70;
    private const ushort VK_F2 = 0x71;
    private const ushort VK_F3 = 0x72;
    private const ushort VK_F4 = 0x73;
    private const ushort VK_F5 = 0x74;
    private const ushort VK_F6 = 0x75;
    private const ushort VK_F7 = 0x76;
    private const ushort VK_F8 = 0x77;
    private const ushort VK_F9 = 0x78;
    private const ushort VK_F10 = 0x79;
    private const ushort VK_F11 = 0x7A;
    private const ushort VK_F12 = 0x7B;

    private const uint INPUT_KEYBOARD = 1;
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
    /// Matches macOS PasteHelper: releases all held modifier/trigger keys before pasting.
    /// Uses SendInput (more reliable than keybd_event on modern Windows).
    /// </summary>
    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            // Capture the foreground window BEFORE we touch clipboard
            // (clipboard operations might briefly steal focus)
            var targetWindow = GetForegroundWindow();

            // Set clipboard on UI thread (must be STA)
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(text);
            });

            // Run key simulation on background thread to avoid blocking UI
            Task.Run(() =>
            {
                try
                {
                    // Release ALL held keys (modifiers + F-keys) before pasting
                    ReleaseAllHeldKeys();
                    Thread.Sleep(150);

                    // Ensure the target window is still focused
                    var currentFg = GetForegroundWindow();
                    if (currentFg != targetWindow && targetWindow != IntPtr.Zero)
                    {
                        SetForegroundWindow(targetWindow);
                        Thread.Sleep(100);
                    }

                    // Simulate Ctrl+V using SendInput (more reliable than keybd_event)
                    var inputs = new INPUT[4];

                    // Ctrl down
                    inputs[0] = MakeKeyInput(VK_CONTROL, false);
                    // V down
                    inputs[1] = MakeKeyInput(VK_V, false);
                    // V up
                    inputs[2] = MakeKeyInput(VK_V, true);
                    // Ctrl up
                    inputs[3] = MakeKeyInput(VK_CONTROL, true);

                    var sent = SendInput(4, inputs, Marshal.SizeOf<INPUT>());
                    FileLogger.Instance.Debug("Clipboard", $"Pasted {text.Length} chars (SendInput sent {sent}/4 events)");

                    if (sent != 4)
                    {
                        FileLogger.Instance.Warning("Clipboard", $"SendInput only sent {sent}/4 events, error: {Marshal.GetLastWin32Error()}");
                    }
                }
                catch (Exception ex)
                {
                    FileLogger.Instance.Error("Clipboard", $"Paste key sim failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Clipboard", $"Paste failed: {ex.Message}");
        }
    }

    private static INPUT MakeKeyInput(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            union = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    /// <summary>
    /// Release all modifier keys AND F-keys that are currently held down.
    /// Matches macOS PasteHelper which releases Option, Shift, Control before Cmd+V.
    /// Also releases F1-F12 since they may be our trigger keys.
    /// </summary>
    private void ReleaseAllHeldKeys()
    {
        ushort[] keysToRelease = {
            VK_CONTROL, VK_LCONTROL, VK_RCONTROL,
            VK_MENU, VK_LMENU, VK_RMENU,
            VK_SHIFT, VK_LSHIFT, VK_RSHIFT,
            VK_LWIN, VK_RWIN,
            VK_F1, VK_F2, VK_F3, VK_F4, VK_F5, VK_F6,
            VK_F7, VK_F8, VK_F9, VK_F10, VK_F11, VK_F12
        };

        var releaseInputs = new List<INPUT>();

        foreach (var key in keysToRelease)
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                releaseInputs.Add(MakeKeyInput(key, true));
                FileLogger.Instance.Debug("Clipboard", $"Releasing held key: 0x{key:X2}");
            }
        }

        if (releaseInputs.Count > 0)
        {
            SendInput((uint)releaseInputs.Count, releaseInputs.ToArray(), Marshal.SizeOf<INPUT>());
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
