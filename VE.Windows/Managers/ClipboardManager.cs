using System.Runtime.InteropServices;
using System.Windows;
using VE.Windows.Helpers;
using VE.Windows.Infrastructure;

namespace VE.Windows.Managers;

/// <summary>
/// Clipboard operations and text pasting via simulated Ctrl+V.
/// Uses Win32 OpenClipboard/SetClipboardData/CloseClipboard directly to avoid
/// WPF Clipboard.SetText lock issues (CLIPBRD_E_CANT_OPEN).
/// Uses AttachThreadInput + SetForegroundWindow + SendInput for reliable paste on Windows.
/// </summary>
public sealed class ClipboardManager : IClipboardManager
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

    // Win32 clipboard API - bypasses WPF Clipboard lock issues
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

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

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint MAPVK_VK_TO_VSC = 0;

    // INPUT struct must include MOUSEINPUT in the union so that
    // Marshal.SizeOf<INPUT>() == 40 on x64, matching Win32's sizeof(INPUT).
    // Without MOUSEINPUT, the struct is only 32 bytes and SendInput silently fails.
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
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

    // Paste cleanup: restore original clipboard after 2-minute timeout (matches macOS)
    private string? _originalClipboardContent;
    private DateTime _pasteTimestamp;
    private Timer? _restoreTimer;
    private const int ClipboardRestoreTimeoutMs = 120_000; // 2 minutes

    private ClipboardManager() { }

    public string? ReadClipboard()
    {
        string? text = null;
        RunOnSTAThread(() =>
        {
            try { text = Clipboard.GetText(); }
                catch (Exception ex) { FileLogger.Instance.Warning("Clipboard", $"Read failed: {ex.Message}"); }
        });
        return text;
    }

    public void WriteClipboard(string text)
    {
        SetClipboardWithRetry(text);
    }

    /// <summary>
    /// Set clipboard text using Win32 API with retry logic.
    /// Avoids WPF Clipboard.SetText which causes CLIPBRD_E_CANT_OPEN errors.
    /// </summary>
    private bool SetClipboardWithRetry(string text, int maxRetries = 10)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (SetClipboardWin32(text))
            {
                FileLogger.Instance.Info("Clipboard", $"Set clipboard OK on attempt {attempt}: {text.Length} chars");
                return true;
            }
            // Wait before retry - clipboard may be held by another process briefly
            Thread.Sleep(50 * attempt); // Increasing backoff: 50, 100, 150, ...
        }
        FileLogger.Instance.Error("Clipboard", $"Failed to set clipboard after {maxRetries} attempts");
        return false;
    }

    /// <summary>
    /// Set clipboard text using raw Win32 API (OpenClipboard/SetClipboardData/CloseClipboard).
    /// This bypasses the WPF Clipboard class entirely.
    /// </summary>
    private static bool SetClipboardWin32(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            var err = Marshal.GetLastWin32Error();
            FileLogger.Instance.Warning("Clipboard", $"OpenClipboard failed: 0x{err:X}");
            return false;
        }

        try
        {
            EmptyClipboard();

            // Allocate global memory for the Unicode string (including null terminator)
            var chars = text.ToCharArray();
            var byteCount = (chars.Length + 1) * 2; // UTF-16, +1 for null terminator
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero)
            {
                FileLogger.Instance.Error("Clipboard", "GlobalAlloc failed");
                return false;
            }

            var ptr = GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                FileLogger.Instance.Error("Clipboard", "GlobalLock failed");
                return false;
            }

            try
            {
                Marshal.Copy(chars, 0, ptr, chars.Length);
                // Write null terminator
                Marshal.WriteInt16(ptr, chars.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            var result = SetClipboardData(CF_UNICODETEXT, hGlobal);
            if (result == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                FileLogger.Instance.Error("Clipboard", "SetClipboardData failed");
                return false;
            }
            // Do NOT call GlobalFree after successful SetClipboardData - OS owns it now

            return true;
        }
        finally
        {
            CloseClipboard();
        }
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
            // Step 0: Save original clipboard content for restore after 2-minute timeout
            SaveOriginalClipboard();

            // Step 1: Set clipboard using Win32 API with retry (NOT WPF Clipboard.SetText)
            if (!SetClipboardWithRetry(text))
            {
                FileLogger.Instance.Error("Clipboard", "Cannot paste - clipboard set failed");
                return;
            }

            FileLogger.Instance.Info("Clipboard", $"Clipboard set: {text.Length} chars, target: 0x{targetWindow:X}");

            // Step 2: Run paste simulation on background thread (matches macOS threading)
            Task.Run(() =>
            {
                try
                {
                    // Step 3: Release ALL held keys (modifiers + F-keys)
                    ReleaseAllHeldKeys();
                    Thread.Sleep(150); // Wait for keys to fully release

                    // Step 4: Restore focus to target window using AttachThreadInput
                    if (targetWindow != IntPtr.Zero)
                    {
                        // Always force foreground — even if we think it's already focused,
                        // the input field inside it may have lost focus
                        ForceForeground(targetWindow);
                    }

                    // Step 5: Wait for focus to settle — apps need time to accept focus
                    // and route it to the active input field
                    Thread.Sleep(200);

                    var fg = GetForegroundWindow();
                    FileLogger.Instance.Info("Clipboard", $"Foreground: 0x{fg:X}, target: 0x{targetWindow:X}, match: {fg == targetWindow}");

                    // If target window didn't get focus, try again
                    if (fg != targetWindow && targetWindow != IntPtr.Zero)
                    {
                        FileLogger.Instance.Warning("Clipboard", "Focus mismatch, retrying ForceForeground");
                        ForceForeground(targetWindow);
                        Thread.Sleep(200);
                    }

                    // Step 6: Simulate Ctrl+V — use SendInput array for atomic delivery
                    // Sending as a batch is more reliable than individual key events
                    var inputs = new INPUT[]
                    {
                        MakeKeyInput(VK_CONTROL, false),
                        MakeKeyInput(VK_V, false),
                        MakeKeyInput(VK_V, true),
                        MakeKeyInput(VK_CONTROL, true)
                    };
                    var cbSize = Marshal.SizeOf<INPUT>();
                    var sent = SendInput((uint)inputs.Length, inputs, cbSize);

                    FileLogger.Instance.Info("Clipboard", $"Pasted {text.Length} chars: SendInput sent={sent}/4, cbSize={cbSize}");

                    // Start 2-minute timer to restore original clipboard
                    StartClipboardRestoreTimer();
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
    /// Save the user's current clipboard content before overwriting with prediction/dictation text.
    /// </summary>
    private void SaveOriginalClipboard()
    {
        try
        {
            _originalClipboardContent = ReadClipboard();
            _pasteTimestamp = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warning("Clipboard", $"SaveOriginalClipboard failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Start a 2-minute timer to restore original clipboard content.
    /// Cancel previous timer if one exists. Matches macOS 2-minute paste cleanup.
    /// </summary>
    private void StartClipboardRestoreTimer()
    {
        _restoreTimer?.Dispose();
        _restoreTimer = new Timer(_ =>
        {
            if (_originalClipboardContent != null)
            {
                FileLogger.Instance.Debug("Clipboard", "Restoring original clipboard content after 2-minute timeout");
                SetClipboardWithRetry(_originalClipboardContent);
                _originalClipboardContent = null;
            }
            _restoreTimer?.Dispose();
            _restoreTimer = null;
        }, null, ClipboardRestoreTimeoutMs, Timeout.Infinite);
    }

    /// <summary>
    /// Cancel clipboard restore if user copies something new.
    /// Call this when user performs their own clipboard operation.
    /// </summary>
    public void CancelClipboardRestore()
    {
        _originalClipboardContent = null;
        _restoreTimer?.Dispose();
        _restoreTimer = null;
    }

    /// <summary>
    /// Create an INPUT struct for a key event (used in batch SendInput calls).
    /// </summary>
    private static INPUT MakeKeyInput(ushort vk, bool keyUp)
    {
        var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            union = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    /// <summary>
    /// Send a single key event via SendInput.
    /// </summary>
    private static void SendSingleKey(ushort vk, bool keyUp)
    {
        var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
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
                        wScan = scan,
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
            if (!thread.Join(TimeSpan.FromSeconds(5)))
            {
                FileLogger.Instance.Warning("Clipboard", "STA thread timed out after 5s");
            }
        }
    }
}
