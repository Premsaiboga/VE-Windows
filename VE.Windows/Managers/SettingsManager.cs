using System.IO;
using Newtonsoft.Json;
using VE.Windows.Helpers;

namespace VE.Windows.Managers;

/// <summary>
/// Persistent settings storage using JSON file in %AppData%/VE/settings.json.
/// </summary>
public sealed class SettingsManager
{
    public static SettingsManager Instance { get; } = new();

    private readonly string _settingsPath;
    private Dictionary<string, object?> _settings;
    private readonly object _lock = new();

    private SettingsManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var veDir = Path.Combine(appData, "VE");
        Directory.CreateDirectory(veDir);
        _settingsPath = Path.Combine(veDir, "settings.json");
        _settings = LoadSettings();
    }

    // Convenience properties for keyboard modifier keys
    public string PredictionModifierKey
    {
        get => Get("PredictionModifierKey", "Ctrl");
        set => Set("PredictionModifierKey", value);
    }

    public string DictationModifierKey
    {
        get => Get("DictationModifierKey", "Shift");
        set => Set("DictationModifierKey", value);
    }

    public string InstructionModifierKey
    {
        get => Get("InstructionModifierKey", "Alt");
        set => Set("InstructionModifierKey", value);
    }

    // Theme
    public string ThemePreference
    {
        get => Get("ThemePreference", "system");
        set => Set("ThemePreference", value);
    }

    // Microphone
    public string SelectedMicrophoneUID
    {
        get => Get("SelectedMicrophoneUID", "system-default");
        set => Set("SelectedMicrophoneUID", value);
    }

    public string SelectedMicrophoneName
    {
        get => Get("SelectedMicrophoneName", "Default");
        set => Set("SelectedMicrophoneName", value);
    }

    public T Get<T>(string key, T defaultValue)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var value) && value != null)
            {
                try
                {
                    // Handle type conversion for JSON deserialized values
                    if (value is T typed)
                        return typed;

                    var json = JsonConvert.SerializeObject(value);
                    var converted = JsonConvert.DeserializeObject<T>(json);
                    return converted ?? defaultValue;
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }
    }

    public void Set(string key, object? value)
    {
        lock (_lock)
        {
            _settings[key] = value;
            SaveSettings();
        }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            _settings.Remove(key);
            SaveSettings();
        }
    }

    private Dictionary<string, object?> LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonConvert.DeserializeObject<Dictionary<string, object?>>(json)
                       ?? new Dictionary<string, object?>();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("SettingsManager", $"Load failed: {ex.Message}");
        }
        return new Dictionary<string, object?>();
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("SettingsManager", $"Save failed: {ex.Message}");
        }
    }
}
