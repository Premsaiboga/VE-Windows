using System.Runtime.InteropServices;
using System.Windows;
using VE.Windows.Helpers;

namespace VE.Windows.Managers;

/// <summary>
/// Clipboard operations and text pasting via simulated Ctrl+V.
/// Matches macOS PasteHelper: releases modifier keys, restores focus, simulates paste.
/// Uses AttachThreadInput + SetForegroundWindow + SendInput for reliable paste on Windows.
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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_V = 0x56;

    // F-keys that might be held as trigger keys
    private const ushort VK_F1 = 0x70;
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
            try { text = Clipboard.GetText(); } catch { }
        });
        return text;
    }

    public void WriteClipboard(string text)
    {
        RunOnSTAThread(() =>
        {
            try { Clipboard.SetText(text); } catch { }
        });
    }

    /// <summary>
    /// Paste text to a specific target window.
    /// </summary>
    public void PasteTextToWindow(string text, IntPtr targetWindow)
    {
        if (string.IsNullOrEmpty(text)) return;
        PasteInternal(text, targetWindow);
    }

    /// <summary>
    /// Paste text to the current foreground window.
    /// </summary>
    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        PasteInternal(text, GetForegroundWindow());
    }

    private void PasteInternal(string text, IntPtr targetWindow)
    {
        try
        {
            // Step 1: Set clipboard on STA thread
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(text);
            });

            FileLogger.Instance.Info("Clipboard", $"Set clipboard: {text.Length} chars, target: 0x{targetWindow:X}");

            // Step 2: Run paste simulation on background thread (matches macOS threading)
            Task.Run(() =>
            {
                try
                {
                    // Step 3: Release ALL held keys (modifiers + F-keys)
                    ReleaseAllHeldKeys();
                    Thread.Sleep(100); // 100ms wait after release (matches macOS)

                    // Step 4: Restore focus to target window using AttachThreadInput
                    // This is the Windows equivalent of macOS's frontmostApp.activate()
                    if (targetWindow != IntPtr.Zero)
                    {
                        var currentFg = GetForegroundWindow();
                        if (currentFg != targetWindow)
                        {
                            ForceForeground(targetWindow);
                        }
                    }

                    // Step 5: Wait for focus to settle
                    Thread.Sleep(50);

                    // Verify target window is focused
                    var fg = GetForegroundWindow();
                    FileLogger.Instance.Info("Clipboard", $"Foreground window: 0x{fg:X}, target: 0x{targetWindow:X}, match: {fg == targetWindow}");

                    // Step 6: Simulate Ctrl+V with DELAYS between events (matches macOS timing)
                    // macOS: 30ms Cmd→V down, 50ms V down→V up, 10ms V up→Cmd up

                    // Ctrl DOWN
                    SendSingleKey(VK_CONTROL, false);
                    Thread.Sleep(30); // 30ms (matches macOS)

                    // V DOWN
                    SendSingleKey(VK_V, false);
                    Thread.Sleep(50); // 50ms (matches macOS)

                    // V UP
                    SendSingleKey(VK_V, true);
                    Thread.Sleep(10); // 10ms (matches macOS)

                    // Ctrl UP
                    SendSingleKey(VK_CONTROL, true);

                    FileLogger.Instance.Info("Clipboard", $"Pasted {text.Length} chars with timed SendInput");
                }
                catch (Exception ex)
                {
                    FileLogger.Instance.Error("Clipboard", $"Paste failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Clipboard", $"PasteInternal failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Send a single key event via SendInput.
    /// </summary>
    private static void SendSingleKey(ushort vk, bool keyUp)
    {
        var input = new INPUT[]
        {
            new INPUT
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
            }
        };
        SendInput(1, input, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Force a window to the foreground using AttachThreadInput trick.
    /// This is the reliable way to call SetForegroundWindow from a background process.
    /// Equivalent to macOS frontmostApp.activate(options: [.activateIgnoringOtherApps])
    /// </summary>
    private static void ForceForeground(IntPtr targetWindow)
    {
        var targetThreadId = GetWindowThreadProcessId(targetWindow, out _);
        var ourThreadId = GetCurrentThreadId();

        if (targetThreadId != ourThreadId)
        {
            AttachThreadInput(ourThreadId, targetThreadId, true);
        }

        SetForegroundWindow(targetWindow);
        BringWindowToTop(targetWindow);

        if (targetThreadId != ourThreadId)
        {
            AttachThreadInput(ourThreadId, targetThreadId, false);
        }
    }

    /// <summary>
    /// Release all held modifier keys AND F-keys.
    /// Matches macOS PasteHelper.releaseAllModifierKeys.
    /// </summary>
    private void ReleaseAllHeldKeys()
    {
        // Modifier keys
        ushort[] modifiers = {
            VK_CONTROL, VK_LCONTROL, VK_RCONTROL,
            VK_MENU, VK_LMENU, VK_RMENU,
            VK_SHIFT, VK_LSHIFT, VK_RSHIFT,
            VK_LWIN, VK_RWIN
        };

        foreach (var key in modifiers)
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                SendSingleKey(key, true);
            }
        }

        // F-keys (F1-F12) - our trigger keys
        for (ushort fk = VK_F1; fk <= VK_F12; fk++)
        {
            if ((GetAsyncKeyState(fk) & 0x8000) != 0)
            {
                SendSingleKey(fk, true);
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
