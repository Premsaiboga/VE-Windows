using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
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
    public Color Background2 => IsDarkMode ? ColorFromHex("#1A1A1A") : ColorFromHex("#FFFFFF");
    public Color SidebarBg => IsDarkMode ? ColorFromHex("#111315") : ColorFromHex("#E8E9EB");
    public Color Card => IsDarkMode ? ColorFromHex("#25292D") : ColorFromHex("#F7F8F8");
    public Color PopupBg => IsDarkMode ? ColorFromHex("#1E2024") : ColorFromHex("#FFFFFF");
    public Color Blue => ColorFromHex("#007CEC");
    public Color BlueHover => Color.FromArgb(204, 0, 124, 236);
    public Color TextPrimary => IsDarkMode ? ColorFromHex("#F4F5F5") : ColorFromHex("#272B30");
    public Color TextSecondary => IsDarkMode ? ColorFromHex("#878E92") : ColorFromHex("#6D737A");
    public Color TextTertiary => IsDarkMode ? ColorFromHex("#A0A4A8") : ColorFromHex("#6D737A");
    public Color TextMuted => IsDarkMode ? ColorFromHex("#555555") : ColorFromHex("#AAAAAA");
    public Color TextBody => IsDarkMode ? ColorFromHex("#DDDDDD") : ColorFromHex("#444444");
    public Color ButtonText => ColorFromHex("#FFFFFF");
    public Color Border => IsDarkMode
        ? Color.FromArgb(26, 244, 245, 245)
        : Color.FromArgb(26, 0, 0, 0);
    public Color BorderMedium => IsDarkMode
        ? Color.FromArgb(51, 255, 255, 255)
        : Color.FromArgb(51, 0, 0, 0);
    public Color BorderStrong => IsDarkMode
        ? Color.FromArgb(85, 255, 255, 255)
        : Color.FromArgb(51, 0, 0, 0);
    public Color Hover => IsDarkMode
        ? Color.FromArgb(26, 255, 255, 255)
        : Color.FromArgb(26, 0, 0, 0);
    public Color RowBg => IsDarkMode
        ? Color.FromArgb(13, 255, 255, 255)
        : Color.FromArgb(13, 0, 0, 0);
    public Color Red => ColorFromHex("#FF4B59");
    public Color Yellow => ColorFromHex("#FFC600");
    public Color Green => ColorFromHex("#00CA48");
    public Color LoginBackground => ColorFromHex("#007CEC");
    public Color ButtonBG2 => IsDarkMode
        ? Color.FromArgb(51, 0, 124, 236)
        : Color.FromArgb(38, 0, 124, 236);
    public Color NotchHintGray => Color.FromArgb(255, 181, 191, 201);
    public Color SurfaceInverse => IsDarkMode ? ColorFromHex("#000000") : ColorFromHex("#FFFFFF");

    // Brush versions for code-behind binding
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
        SetupSystemThemeObserver();
    }

    /// <summary>
    /// Called from App.OnStartup after Application.Resources are loaded.
    /// Must run on UI thread after the resource dictionary is available.
    /// </summary>
    public void Initialize()
    {
        ApplyThemeResources();
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
                    Application.Current?.Dispatcher.Invoke(ApplyThemeResources);
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
        ApplyThemeResources();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        NotifyAllColors();
    }

    /// <summary>
    /// Swaps all semantic brush resources in Application.Current.Resources.
    /// DynamicResource references in XAML will auto-update.
    /// </summary>
    private void ApplyThemeResources()
    {
        var res = Application.Current?.Resources;
        if (res == null) return;

        // Backgrounds
        res["ThemeBg"] = new SolidColorBrush(Background);
        res["ThemeBg2"] = new SolidColorBrush(Background2);
        res["ThemeSidebarBg"] = new SolidColorBrush(SidebarBg);
        res["ThemeCard"] = new SolidColorBrush(Card);
        res["ThemePopupBg"] = new SolidColorBrush(PopupBg);
        res["ThemeInputBg"] = new SolidColorBrush(Card);

        // Text
        res["ThemeTextPrimary"] = new SolidColorBrush(TextPrimary);
        res["ThemeTextSecondary"] = new SolidColorBrush(TextSecondary);
        res["ThemeTextTertiary"] = new SolidColorBrush(TextTertiary);
        res["ThemeTextMuted"] = new SolidColorBrush(TextMuted);
        res["ThemeTextBody"] = new SolidColorBrush(TextBody);

        // Borders
        res["ThemeBorder"] = new SolidColorBrush(Border);
        res["ThemeBorderMedium"] = new SolidColorBrush(BorderMedium);
        res["ThemeBorderStrong"] = new SolidColorBrush(BorderStrong);

        // Interactive
        res["ThemeHover"] = new SolidColorBrush(Hover);
        res["ThemeRowBg"] = new SolidColorBrush(RowBg);
        res["ThemeOverlay"] = new SolidColorBrush(IsDarkMode
            ? Color.FromArgb(128, 0, 0, 0)
            : Color.FromArgb(64, 0, 0, 0));

        // Notch
        res["ThemeNotchBg"] = new SolidColorBrush(IsDarkMode
            ? ColorFromHex("#000000") : ColorFromHex("#FFFFFF"));
        res["ThemeNotchText"] = new SolidColorBrush(IsDarkMode
            ? ColorFromHex("#FFFFFF") : ColorFromHex("#272B30"));
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
        OnPropertyChanged(nameof(IsDarkMode));
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
