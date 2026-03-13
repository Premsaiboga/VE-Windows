using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Microsoft.Win32;
using VE.Windows.Models;

namespace VE.Windows.Theme;

public sealed class ThemeManager : INotifyPropertyChanged
{
    public static ThemeManager Instance { get; } = new();

    private bool _isDarkMode;
    private ThemePreference _themePreference;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ThemeChanged;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        private set { _isDarkMode = value; OnPropertyChanged(); }
    }

    public ThemePreference CurrentTheme
    {
        get => _themePreference;
        set
        {
            _themePreference = value;
            SettingsManager.Instance.ThemePreference = value;
            ApplyTheme();
            OnPropertyChanged();
        }
    }

    // Theme-aware colors matching macOS VEColors
    public Color Background => IsDarkMode ? ColorFromHex("#151719") : ColorFromHex("#F4F5F5");
    public Color Card => IsDarkMode ? ColorFromHex("#25292D") : ColorFromHex("#FFFFFF");
    public Color Blue => ColorFromHex("#007CEC");
    public Color BlueHover => Color.FromArgb(204, 0, 124, 236); // 80% opacity
    public Color TextPrimary => IsDarkMode ? ColorFromHex("#F4F5F5") : ColorFromHex("#272B30");
    public Color TextSecondary => ColorFromHex("#878E92");
    public Color ButtonText => ColorFromHex("#FFFFFF");
    public Color Border => IsDarkMode
        ? Color.FromArgb(26, 244, 245, 245)   // F4F5F5 @ 10%
        : Color.FromArgb(26, 0, 0, 0);         // 000000 @ 10%
    public Color Red => ColorFromHex("#FF4B59");
    public Color Yellow => ColorFromHex("#FFC600");
    public Color Green => ColorFromHex("#00CA48");
    public Color LoginBackground => ColorFromHex("#007CEC");
    public Color ButtonBG2 => IsDarkMode
        ? Color.FromArgb(51, 0, 124, 236)   // 007CEC @ 20%
        : Color.FromArgb(38, 0, 124, 236);   // 007CEC @ 15%
    public Color NotchHintGray => Color.FromArgb(255, 181, 191, 201);
    public Color SurfaceInverse => IsDarkMode ? ColorFromHex("#000000") : ColorFromHex("#FFFFFF");

    // Brush versions for WPF binding
    public SolidColorBrush BackgroundBrush => new(Background);
    public SolidColorBrush CardBrush => new(Card);
    public SolidColorBrush BlueBrush => new(Blue);
    public SolidColorBrush TextPrimaryBrush => new(TextPrimary);
    public SolidColorBrush TextSecondaryBrush => new(TextSecondary);
    public SolidColorBrush ButtonTextBrush => new(ButtonText);
    public SolidColorBrush BorderBrush => new(Border);
    public SolidColorBrush RedBrush => new(Red);

    private ThemeManager()
    {
        _themePreference = SettingsManager.Instance.ThemePreference;
        _isDarkMode = DetectDarkMode();
        ApplyTheme();
        SetupSystemThemeObserver();
    }

    private bool DetectDarkMode()
    {
        return _themePreference switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            _ => IsSystemDarkMode()
        };
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intVal && intVal == 0;
        }
        catch { return false; }
    }

    private void SetupSystemThemeObserver()
    {
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _themePreference == ThemePreference.System)
            {
                var newDark = IsSystemDarkMode();
                if (IsDarkMode != newDark)
                {
                    IsDarkMode = newDark;
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                    NotifyAllColors();
                }
            }
        };
    }

    public void ToggleTheme()
    {
        CurrentTheme = CurrentTheme switch
        {
            ThemePreference.System => ThemePreference.Dark,
            ThemePreference.Dark => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.System,
            _ => ThemePreference.System
        };
    }

    public void SetSystemTheme() => CurrentTheme = ThemePreference.System;

    private void ApplyTheme()
    {
        IsDarkMode = _themePreference switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            _ => IsSystemDarkMode()
        };
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        NotifyAllColors();
    }

    private void NotifyAllColors()
    {
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(Card));
        OnPropertyChanged(nameof(TextPrimary));
        OnPropertyChanged(nameof(TextSecondary));
        OnPropertyChanged(nameof(Border));
        OnPropertyChanged(nameof(SurfaceInverse));
        OnPropertyChanged(nameof(BackgroundBrush));
        OnPropertyChanged(nameof(CardBrush));
        OnPropertyChanged(nameof(TextPrimaryBrush));
        OnPropertyChanged(nameof(TextSecondaryBrush));
        OnPropertyChanged(nameof(BorderBrush));
    }

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return hex.Length switch
        {
            6 => Color.FromRgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16)),
            8 => Color.FromArgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16)),
            _ => Colors.Black
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
