using System.Diagnostics;
using System.Runtime.InteropServices;
using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Managers;

/// <summary>
/// Global keyboard hook for modifier key detection.
/// Equivalent to macOS LowLevelKeyTap + KeyboardMonitor.
/// Uses Win32 SetWindowsHookEx with WH_KEYBOARD_LL.
/// </summary>
public sealed class KeyboardHookManager : IDisposable
{
    public static KeyboardHookManager Instance { get; } = new();

    // Win32 constants
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    // Virtual key codes
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_MENU = 0x12;      // Alt
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_RETURN = 0x0D;
    private const int VK_F1 = 0x70;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private readonly Dictionary<int, DateTime> _keyDownTimes = new();
    private DateTime _lastTapTime = DateTime.MinValue;
    private int _lastTapKey;
    private const int HoldThresholdMs = 350;
    private const int DoubleTapThresholdMs = 500;

    // Events
    public event EventHandler? OnPredictionTriggered;
    public event EventHandler? OnPredictionReleased;
    public event EventHandler? OnDictationTriggered;
    public event EventHandler? OnDictationReleased;
    public event EventHandler? OnInstructionTriggered;
    public event EventHandler? OnInstructionReleased;
    public event EventHandler? OnMeetingToggled;
    public event EventHandler? OnEscapePressed;
    public event EventHandler? OnEnterPressed;

    public bool IsControlHeld { get; private set; }
    public bool IsAltHeld { get; private set; }

    public enum ActiveAction { None, Prediction, Dictation, Instruction, Meeting }
    public ActiveAction CurrentAction { get; private set; } = ActiveAction.None;

    private KeyboardHookManager() { }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        _hookProc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
            GetModuleHandle(curModule.ModuleName!), 0);

        if (_hookId == IntPtr.Zero)
        {
            FileLogger.Instance.Error("KeyboardHook", "Failed to install keyboard hook");
        }
        else
        {
            FileLogger.Instance.Info("KeyboardHook", "Keyboard hook installed");
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            FileLogger.Instance.Info("KeyboardHook", "Keyboard hook removed");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var vkCode = (int)hookStruct.vkCode;
            var isKeyDown = (int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN;
            var isKeyUp = (int)wParam == WM_KEYUP || (int)wParam == WM_SYSKEYUP;

            if (isKeyDown)
            {
                HandleKeyDown(vkCode);
            }
            else if (isKeyUp)
            {
                HandleKeyUp(vkCode);
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void HandleKeyDown(int vkCode)
    {
        // Track key press time
        if (!_keyDownTimes.ContainsKey(vkCode))
        {
            _keyDownTimes[vkCode] = DateTime.UtcNow;
        }

        // Escape
        if (vkCode == VK_ESCAPE)
        {
            OnEscapePressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Enter
        if (vkCode == VK_RETURN)
        {
            OnEnterPressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Check for modifier key holds
        var settings = SettingsManager.Instance;
        var predKey = settings.PredictionModifierKey;
        var dictKey = settings.DictationModifierKey;
        var instrKey = settings.InstructionModifierKey;

        if (IsModifierKey(vkCode, predKey) && CurrentAction == ActiveAction.None)
        {
            var elapsed = (DateTime.UtcNow - _keyDownTimes.GetValueOrDefault(vkCode, DateTime.UtcNow)).TotalMilliseconds;
            if (elapsed >= HoldThresholdMs)
            {
                CurrentAction = ActiveAction.Prediction;
                IsControlHeld = true;
                OnPredictionTriggered?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (IsModifierKey(vkCode, dictKey) && CurrentAction == ActiveAction.None)
        {
            var elapsed = (DateTime.UtcNow - _keyDownTimes.GetValueOrDefault(vkCode, DateTime.UtcNow)).TotalMilliseconds;
            if (elapsed >= HoldThresholdMs)
            {
                CurrentAction = ActiveAction.Dictation;
                OnDictationTriggered?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (IsModifierKey(vkCode, instrKey) && CurrentAction == ActiveAction.None)
        {
            var elapsed = (DateTime.UtcNow - _keyDownTimes.GetValueOrDefault(vkCode, DateTime.UtcNow)).TotalMilliseconds;
            if (elapsed >= HoldThresholdMs)
            {
                CurrentAction = ActiveAction.Instruction;
                IsAltHeld = true;
                OnInstructionTriggered?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void HandleKeyUp(int vkCode)
    {
        var holdDuration = _keyDownTimes.TryGetValue(vkCode, out var downTime)
            ? (DateTime.UtcNow - downTime).TotalMilliseconds
            : 0;
        _keyDownTimes.Remove(vkCode);

        var settings = SettingsManager.Instance;

        // Check for double-tap (meeting toggle) - using F1 as configurable meeting key
        if (vkCode == VK_F1 && holdDuration < HoldThresholdMs)
        {
            var now = DateTime.UtcNow;
            if (_lastTapKey == vkCode && (now - _lastTapTime).TotalMilliseconds < DoubleTapThresholdMs)
            {
                OnMeetingToggled?.Invoke(this, EventArgs.Empty);
                _lastTapTime = DateTime.MinValue;
            }
            else
            {
                _lastTapTime = now;
                _lastTapKey = vkCode;
            }
        }

        // Release prediction
        if (IsModifierKey(vkCode, settings.PredictionModifierKey) && CurrentAction == ActiveAction.Prediction)
        {
            CurrentAction = ActiveAction.None;
            IsControlHeld = false;
            OnPredictionReleased?.Invoke(this, EventArgs.Empty);
        }
        // Release dictation
        else if (IsModifierKey(vkCode, settings.DictationModifierKey) && CurrentAction == ActiveAction.Dictation)
        {
            CurrentAction = ActiveAction.None;
            OnDictationReleased?.Invoke(this, EventArgs.Empty);
        }
        // Release instruction
        else if (IsModifierKey(vkCode, settings.InstructionModifierKey) && CurrentAction == ActiveAction.Instruction)
        {
            CurrentAction = ActiveAction.None;
            IsAltHeld = false;
            OnInstructionReleased?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsModifierKey(int vkCode, ModifierKeyOption option)
    {
        return option switch
        {
            ModifierKeyOption.Control => vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL,
            ModifierKeyOption.Alt => vkCode is VK_MENU or VK_LMENU or VK_RMENU,
            ModifierKeyOption.Shift => vkCode is VK_SHIFT or VK_LSHIFT or VK_RSHIFT,
            ModifierKeyOption.Win => vkCode is VK_LWIN or VK_RWIN,
            _ => false
        };
    }

    public void Dispose()
    {
        Stop();
    }
}
