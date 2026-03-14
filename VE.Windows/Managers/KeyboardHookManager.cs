using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Managers;

/// <summary>
/// Global keyboard hook for shortcut key detection.
/// Uses Win32 SetWindowsHookEx with WH_KEYBOARD_LL.
/// Supports configurable keys (default: F1 for prediction, F2 for dictation).
/// F1 tap = screenshot prediction, F1 hold = voice+screenshot prediction.
/// F2 hold = dictation recording, F2 release = process dictation.
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

    private const int VK_ESCAPE = 0x1B;
    private const int VK_RETURN = 0x0D;

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

    // Hold detection
    private int _heldKey;
    private DateTime _heldKeyStartTime;
    private DispatcherTimer? _holdTimer;
    private bool _holdFired;
    private bool _keyIsDown; // Track if the key is physically down (handles repeated keydown)

    private const int HoldThresholdMs = 350;

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

    public enum ActiveAction { None, Prediction, PredictionTap, Dictation, Instruction, Meeting }
    public ActiveAction CurrentAction { get; private set; } = ActiveAction.None;

    /// <summary>
    /// Map of key names to virtual key codes for UI display and configuration.
    /// </summary>
    public static readonly Dictionary<string, int> AvailableKeys = new()
    {
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B,
        ["Ctrl"] = 0x11,
        ["Alt"] = 0x12,
        ["Shift"] = 0x10,
    };

    public static string GetKeyName(int vkCode)
    {
        foreach (var kv in AvailableKeys)
        {
            if (kv.Value == vkCode) return kv.Key;
        }
        return $"Key 0x{vkCode:X2}";
    }

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

        // Timer to detect hold (checks every 50ms)
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
        if (_heldKey == 0 || _holdFired) return;

        var elapsed = (DateTime.UtcNow - _heldKeyStartTime).TotalMilliseconds;
        if (elapsed < HoldThresholdMs) return;

        _holdFired = true;
        _holdTimer?.Stop();

        var settings = SettingsManager.Instance;
        var predictionKey = settings.PredictionKeyCode;
        var dictationKey = settings.DictationKeyCode;

        if (MatchesKey(_heldKey, predictionKey) && CurrentAction == ActiveAction.None)
        {
            CurrentAction = ActiveAction.Prediction;
            FileLogger.Instance.Info("KeyboardHook", $"Prediction triggered (hold {GetKeyName(_heldKey)})");
            OnPredictionTriggered?.Invoke(this, EventArgs.Empty);
        }
        else if (MatchesKey(_heldKey, dictationKey) && CurrentAction == ActiveAction.None)
        {
            CurrentAction = ActiveAction.Dictation;
            FileLogger.Instance.Info("KeyboardHook", $"Dictation triggered (hold {GetKeyName(_heldKey)})");
            OnDictationTriggered?.Invoke(this, EventArgs.Empty);
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
        // Escape - immediate
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

        var settings = SettingsManager.Instance;
        var predictionKey = settings.PredictionKeyCode;
        var dictationKey = settings.DictationKeyCode;

        // Check if this is a configured key
        if (!MatchesKey(vkCode, predictionKey) && !MatchesKey(vkCode, dictationKey)) return;

        // Ignore repeated keydown for same key (non-modifier keys send repeats)
        if (_keyIsDown && _heldKey != 0 && MatchesKey(vkCode, _heldKey)) return;

        // Start hold tracking
        _heldKey = vkCode;
        _heldKeyStartTime = DateTime.UtcNow;
        _holdFired = false;
        _keyIsDown = true;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            _holdTimer?.Start());
    }

    private void HandleKeyUp(int vkCode)
    {
        var settings = SettingsManager.Instance;
        var predictionKey = settings.PredictionKeyCode;
        var dictationKey = settings.DictationKeyCode;

        // Only handle configured keys
        if (!MatchesKey(vkCode, predictionKey) && !MatchesKey(vkCode, dictationKey)) return;

        // Stop hold timer
        if (_heldKey != 0 && MatchesKey(vkCode, _heldKey))
        {
            _heldKey = 0;
            _keyIsDown = false;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                _holdTimer?.Stop());
        }

        // Release active actions on UI thread
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (MatchesKey(vkCode, predictionKey))
            {
                if (CurrentAction == ActiveAction.Prediction)
                {
                    // Was held long enough for voice prediction - release it
                    CurrentAction = ActiveAction.None;
                    OnPredictionReleased?.Invoke(this, EventArgs.Empty);
                }
                else if (CurrentAction == ActiveAction.None && !_holdFired)
                {
                    // Quick tap: released before hold threshold
                    var elapsed = (DateTime.UtcNow - _heldKeyStartTime).TotalMilliseconds;
                    if (elapsed > 30 && elapsed < HoldThresholdMs)
                    {
                        _holdFired = true; // Prevent double-fire
                        FileLogger.Instance.Info("KeyboardHook", $"Prediction tap detected ({elapsed:F0}ms)");
                        CurrentAction = ActiveAction.PredictionTap;
                        OnPredictionTapped?.Invoke(this, EventArgs.Empty);
                        CurrentAction = ActiveAction.None;
                    }
                }
            }
            else if (MatchesKey(vkCode, dictationKey))
            {
                if (CurrentAction == ActiveAction.Dictation)
                {
                    // Dictation hold released - stop recording
                    CurrentAction = ActiveAction.None;
                    OnDictationReleased?.Invoke(this, EventArgs.Empty);
                }
            }
        });
    }

    /// <summary>
    /// Check if a virtual key code matches a configured key.
    /// For modifier keys, matches left/right/generic variants.
    /// </summary>
    private static bool MatchesKey(int pressedKey, int configuredKey)
    {
        if (pressedKey == configuredKey) return true;

        // Handle modifier key families (left/right/generic all match)
        return configuredKey switch
        {
            0x11 => pressedKey is 0x11 or 0xA2 or 0xA3, // VK_CONTROL, VK_LCONTROL, VK_RCONTROL
            0x12 => pressedKey is 0x12 or 0xA4 or 0xA5, // VK_MENU, VK_LMENU, VK_RMENU
            0x10 => pressedKey is 0x10 or 0xA0 or 0xA1, // VK_SHIFT, VK_LSHIFT, VK_RSHIFT
            _ => false
        };
    }

    public void Dispose()
    {
        Stop();
    }
}
