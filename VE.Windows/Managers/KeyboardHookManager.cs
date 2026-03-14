using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Managers;

/// <summary>
/// Global keyboard hook for modifier key detection.
/// Uses Win32 SetWindowsHookEx with WH_KEYBOARD_LL.
/// Hold detection uses a DispatcherTimer since LL hook doesn't send
/// repeated keydown events for modifier keys.
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

    // Hold detection via timer
    private int _heldModifierKey;
    private DateTime _heldModifierStartTime;
    private DispatcherTimer? _holdTimer;
    private bool _holdFired;

    // Double-tap detection
    private DateTime _lastTapTime = DateTime.MinValue;
    private int _lastTapKey;

    private const int HoldThresholdMs = 350;
    private const int DoubleTapThresholdMs = 500;

    // Events
    public event EventHandler? OnPredictionTriggered;   // Hold: voice+screenshot prediction
    public event EventHandler? OnPredictionReleased;
    public event EventHandler? OnPredictionTapped;      // Quick tap: screenshot-only prediction
    public event EventHandler? OnDictationTriggered;
    public event EventHandler? OnDictationReleased;
    public event EventHandler? OnInstructionTriggered;
    public event EventHandler? OnInstructionReleased;
    public event EventHandler? OnMeetingToggled;
    public event EventHandler? OnEscapePressed;
    public event EventHandler? OnEnterPressed;

    public bool IsControlHeld { get; private set; }
    public bool IsAltHeld { get; private set; }

    public enum ActiveAction { None, Prediction, PredictionTap, Dictation, Instruction, Meeting }
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
            FileLogger.Instance.Error("KeyboardHook", $"Failed to install hook. Error: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            FileLogger.Instance.Info("KeyboardHook", "Keyboard hook installed successfully");
        }

        // Timer to detect modifier hold (checks every 50ms)
        _holdTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _holdTimer.Tick += HoldTimer_Tick;
    }

    public void Stop()
    {
        _holdTimer?.Stop();
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            FileLogger.Instance.Info("KeyboardHook", "Keyboard hook removed");
        }
    }

    private void HoldTimer_Tick(object? sender, EventArgs e)
    {
        if (_heldModifierKey == 0 || _holdFired) return;

        var elapsed = (DateTime.UtcNow - _heldModifierStartTime).TotalMilliseconds;
        if (elapsed < HoldThresholdMs) return;

        _holdFired = true;
        _holdTimer?.Stop();

        var settings = SettingsManager.Instance;

        if (IsModifierKey(_heldModifierKey, settings.PredictionModifierKey) && CurrentAction == ActiveAction.None)
        {
            CurrentAction = ActiveAction.Prediction;
            IsControlHeld = true;
            FileLogger.Instance.Info("KeyboardHook", "Prediction triggered");
            OnPredictionTriggered?.Invoke(this, EventArgs.Empty);
        }
        else if (IsModifierKey(_heldModifierKey, settings.DictationModifierKey) && CurrentAction == ActiveAction.None)
        {
            CurrentAction = ActiveAction.Dictation;
            FileLogger.Instance.Info("KeyboardHook", "Dictation triggered");
            OnDictationTriggered?.Invoke(this, EventArgs.Empty);
        }
        else if (IsModifierKey(_heldModifierKey, settings.InstructionModifierKey) && CurrentAction == ActiveAction.None)
        {
            CurrentAction = ActiveAction.Instruction;
            IsAltHeld = true;
            FileLogger.Instance.Info("KeyboardHook", "Instruction triggered");
            OnInstructionTriggered?.Invoke(this, EventArgs.Empty);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var vkCode = (int)hookStruct.vkCode;
            var msg = (int)wParam;
            var isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            var isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            try
            {
                if (isKeyDown) HandleKeyDown(vkCode);
                else if (isKeyUp) HandleKeyUp(vkCode);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("KeyboardHook", $"Error: {ex.Message}");
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void HandleKeyDown(int vkCode)
    {
        // Escape - immediate, dispatch to UI thread
        if (vkCode == VK_ESCAPE)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                OnEscapePressed?.Invoke(this, EventArgs.Empty));
            return;
        }

        // Enter - immediate
        if (vkCode == VK_RETURN)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                OnEnterPressed?.Invoke(this, EventArgs.Empty));
            return;
        }

        // Only track modifier keys
        if (!IsAnyModifierKey(vkCode)) return;

        // Already tracking this key or a related one
        if (_heldModifierKey != 0 && IsRelatedModifierKey(vkCode, _heldModifierKey)) return;

        // Start hold tracking
        _heldModifierKey = vkCode;
        _heldModifierStartTime = DateTime.UtcNow;
        _holdFired = false;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            _holdTimer?.Start());
    }

    private void HandleKeyUp(int vkCode)
    {
        var settings = SettingsManager.Instance;

        // Double-tap F1 for meeting toggle
        if (vkCode == VK_F1)
        {
            var now = DateTime.UtcNow;
            if (_lastTapKey == vkCode && (now - _lastTapTime).TotalMilliseconds < DoubleTapThresholdMs)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    OnMeetingToggled?.Invoke(this, EventArgs.Empty));
                _lastTapTime = DateTime.MinValue;
            }
            else
            {
                _lastTapTime = now;
                _lastTapKey = vkCode;
            }
        }

        // Stop hold timer if this is the tracked modifier
        if (_heldModifierKey != 0 && (vkCode == _heldModifierKey || IsRelatedModifierKey(vkCode, _heldModifierKey)))
        {
            _heldModifierKey = 0;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                _holdTimer?.Stop());
        }

        // Release active actions on UI thread
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (IsModifierKey(vkCode, settings.PredictionModifierKey))
            {
                if (CurrentAction == ActiveAction.Prediction)
                {
                    // Was held long enough for voice prediction - release it
                    CurrentAction = ActiveAction.None;
                    IsControlHeld = false;
                    OnPredictionReleased?.Invoke(this, EventArgs.Empty);
                }
                else if (CurrentAction == ActiveAction.None && !_holdFired)
                {
                    // Quick tap: released before hold threshold (350ms) - screenshot-only prediction
                    var elapsed = (DateTime.UtcNow - _heldModifierStartTime).TotalMilliseconds;
                    if (elapsed > 50 && elapsed < HoldThresholdMs) // Ignore very short accidental presses
                    {
                        FileLogger.Instance.Info("KeyboardHook", $"Prediction tap detected ({elapsed:F0}ms)");
                        CurrentAction = ActiveAction.PredictionTap;
                        OnPredictionTapped?.Invoke(this, EventArgs.Empty);
                        CurrentAction = ActiveAction.None;
                    }
                }
            }
            else if (IsModifierKey(vkCode, settings.DictationModifierKey) && CurrentAction == ActiveAction.Dictation)
            {
                CurrentAction = ActiveAction.None;
                OnDictationReleased?.Invoke(this, EventArgs.Empty);
            }
            else if (IsModifierKey(vkCode, settings.InstructionModifierKey) && CurrentAction == ActiveAction.Instruction)
            {
                CurrentAction = ActiveAction.None;
                IsAltHeld = false;
                OnInstructionReleased?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private static bool IsAnyModifierKey(int vkCode)
    {
        return vkCode is VK_CONTROL or VK_LCONTROL or VK_RCONTROL
            or VK_MENU or VK_LMENU or VK_RMENU
            or VK_SHIFT or VK_LSHIFT or VK_RSHIFT
            or VK_LWIN or VK_RWIN;
    }

    private static bool IsRelatedModifierKey(int vk1, int vk2)
    {
        return GetModifierFamily(vk1) == GetModifierFamily(vk2) && GetModifierFamily(vk1) != 0;
    }

    private static int GetModifierFamily(int vkCode)
    {
        return vkCode switch
        {
            VK_CONTROL or VK_LCONTROL or VK_RCONTROL => VK_CONTROL,
            VK_MENU or VK_LMENU or VK_RMENU => VK_MENU,
            VK_SHIFT or VK_LSHIFT or VK_RSHIFT => VK_SHIFT,
            VK_LWIN or VK_RWIN => VK_LWIN,
            _ => 0
        };
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
