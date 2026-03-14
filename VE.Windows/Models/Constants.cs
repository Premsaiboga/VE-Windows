using System.IO;
using Newtonsoft.Json;

namespace VE.Windows.Models;

public enum ModifierKeyOption
{
    None,
    Control,
    Alt,
    Shift,
    Win
}

public enum WindowHeightMode
{
    MatchRealNotchSize,
    MatchMenuBar,
    Custom
}

public enum MemoryDisplayMode
{
    Grid,
    List
}

public enum MemorySortOption
{
    RecentlyCreated,
    RecentlyModified,
    Alphabetical
}

public enum ThemePreference
{
    System,
    Light,
    Dark
}

public enum SliderColorEnum
{
    White,
    AccentColor,
    AlbumArt
}

public enum DownloadIndicatorStyle
{
    Progress,
    Percentage
}

public enum DownloadIconStyle
{
    OnlyAppIcon,
    FileIcon
}

/// <summary>
/// Centralized settings manager using JSON file storage in %AppData%/VE/
/// Equivalent to macOS Defaults.Keys
/// </summary>
public sealed class SettingsManager
{
    public static SettingsManager Instance { get; } = new();

    private readonly string _settingsPath;
    private Dictionary<string, object> _settings;
    private readonly object _lock = new();

    private SettingsManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var veDir = Path.Combine(appData, "VE");
        Directory.CreateDirectory(veDir);
        _settingsPath = Path.Combine(veDir, "settings.json");
        _settings = LoadSettings();
    }

    private Dictionary<string, object> LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
                       ?? new Dictionary<string, object>();
            }
        }
        catch { }
        return new Dictionary<string, object>();
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }

    public T Get<T>(string key, T defaultValue)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is T typed) return typed;
                    var json = JsonConvert.SerializeObject(value);
                    return JsonConvert.DeserializeObject<T>(json) ?? defaultValue;
                }
                catch { return defaultValue; }
            }
            return defaultValue;
        }
    }

    public void Set<T>(string key, T value)
    {
        lock (_lock)
        {
            _settings[key] = value!;
            SaveSettings();
        }
    }

    public bool Has(string key)
    {
        lock (_lock) { return _settings.ContainsKey(key); }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            _settings.Remove(key);
            SaveSettings();
        }
    }

    // MARK: General
    public bool MenubarIcon { get => Get("MenubarIcon", true); set => Set("MenubarIcon", value); }
    public bool ShowOnAllDisplays { get => Get("ShowOnAllDisplays", false); set => Set("ShowOnAllDisplays", value); }
    public bool AutomaticallySwitchDisplay { get => Get("AutomaticallySwitchDisplay", true); set => Set("AutomaticallySwitchDisplay", value); }

    // MARK: Behavior
    public bool EnableHaptics { get => Get("EnableHaptics", true); set => Set("EnableHaptics", value); }
    public bool WelcomeMessageShown { get => Get("WelcomeMessageShown", false); set => Set("WelcomeMessageShown", value); }
    public bool ShowOnLockScreen { get => Get("ShowOnLockScreen", false); set => Set("ShowOnLockScreen", value); }

    // MARK: Appearance
    public bool ShowEmojis { get => Get("ShowEmojis", false); set => Set("ShowEmojis", value); }
    public bool SettingsIconInNotch { get => Get("SettingsIconInNotch", true); set => Set("SettingsIconInNotch", value); }
    public bool LightingEffect { get => Get("LightingEffect", true); set => Set("LightingEffect", value); }
    public bool EnableShadow { get => Get("EnableShadow", true); set => Set("EnableShadow", value); }
    public bool CornerRadiusScaling { get => Get("CornerRadiusScaling", true); set => Set("CornerRadiusScaling", value); }

    // MARK: Theme
    public ThemePreference ThemePreference
    {
        get => Get("ThemePreference", ThemePreference.System);
        set => Set("ThemePreference", value);
    }

    // MARK: Gestures
    public bool EnableGestures { get => Get("EnableGestures", true); set => Set("EnableGestures", value); }
    public bool CloseGestureEnabled { get => Get("CloseGestureEnabled", true); set => Set("CloseGestureEnabled", value); }
    public double GestureSensitivity { get => Get("GestureSensitivity", 200.0); set => Set("GestureSensitivity", value); }

    // MARK: Modifier Keys (legacy)
    public ModifierKeyOption PredictionModifierKey
    {
        get => Get("PredictionModifierKey", ModifierKeyOption.Control);
        set => Set("PredictionModifierKey", value);
    }
    public ModifierKeyOption DictationModifierKey
    {
        get => Get("DictationModifierKey", ModifierKeyOption.Shift);
        set => Set("DictationModifierKey", value);
    }
    public ModifierKeyOption InstructionModifierKey
    {
        get => Get("InstructionModifierKey", ModifierKeyOption.Alt);
        set => Set("InstructionModifierKey", value);
    }

    // MARK: Configurable Shortcut Keys (virtual key codes)
    // Default: F1 (0x70) for prediction, F2 (0x71) for dictation
    public int PredictionKeyCode
    {
        get => Get("PredictionKeyCode", 0x70); // VK_F1
        set => Set("PredictionKeyCode", value);
    }
    public int DictationKeyCode
    {
        get => Get("DictationKeyCode", 0x71); // VK_F2
        set => Set("DictationKeyCode", value);
    }

    // MARK: Microphone
    public string? SelectedMicrophoneUID
    {
        get => Get<string?>("SelectedMicrophoneUID", null);
        set => Set("SelectedMicrophoneUID", value);
    }
    public string? SelectedMicrophoneName
    {
        get => Get<string?>("SelectedMicrophoneName", null);
        set => Set("SelectedMicrophoneName", value);
    }
    public bool PreferBuiltInOverBluetooth
    {
        get => Get("PreferBuiltInOverBluetooth", true);
        set => Set("PreferBuiltInOverBluetooth", value);
    }

    // MARK: Meeting Audio
    public bool EnableMeetingScreenAudio
    {
        get => Get("EnableMeetingScreenAudio", true);
        set => Set("EnableMeetingScreenAudio", value);
    }

    // MARK: Memory
    public MemoryDisplayMode MemoryDisplayMode
    {
        get => Get("MemoryDisplayMode", MemoryDisplayMode.Grid);
        set => Set("MemoryDisplayMode", value);
    }
    public MemorySortOption MemorySortOption
    {
        get => Get("MemorySortOption", MemorySortOption.RecentlyCreated);
        set => Set("MemorySortOption", value);
    }

    // MARK: Custom Colors
    public bool UseCustomPredictionColors { get => Get("UseCustomPredictionColors", false); set => Set("UseCustomPredictionColors", value); }
    public string? PredictionBackgroundColor { get => Get<string?>("PredictionBackgroundColor", null); set => Set("PredictionBackgroundColor", value); }
    public string? PredictionTextColor { get => Get<string?>("PredictionTextColor", null); set => Set("PredictionTextColor", value); }

    public bool UseCustomDictationColors { get => Get("UseCustomDictationColors", false); set => Set("UseCustomDictationColors", value); }
    public string? DictationBackgroundColor { get => Get<string?>("DictationBackgroundColor", null); set => Set("DictationBackgroundColor", value); }
    public string? DictationTextColor { get => Get<string?>("DictationTextColor", null); set => Set("DictationTextColor", value); }

    public bool UseCustomIdleColors { get => Get("UseCustomIdleColors", false); set => Set("UseCustomIdleColors", value); }
    public string? IdleBackgroundColor { get => Get<string?>("IdleBackgroundColor", null); set => Set("IdleBackgroundColor", value); }
}