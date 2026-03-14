using System.Windows;
using System.Windows.Controls;
using VE.Windows.Managers;
using VE.Windows.Theme;
using VE.Windows.ViewModels;

namespace VE.Windows.Views.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm = new();

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        ApplyTheme();

        ThemeManager.Instance.ThemeChanged += (s, e) => Dispatcher.Invoke(ApplyTheme);
        NavigateToSection("home");
    }

    private void ApplyTheme()
    {
        var tm = ThemeManager.Instance;
        Background = new System.Windows.Media.SolidColorBrush(tm.Background);
    }

    public void NavigateToSection(string section)
    {
        _vm.SelectedSection = section;

        switch (section)
        {
            case "home":
                ContentArea.Content = CreatePlaceholderView("Home", "Welcome to VE Dashboard");
                break;
            case "chat":
                ContentArea.Content = new FloatingWindow.ChatView();
                break;
            case "shortcuts":
                ContentArea.Content = CreateShortcutsView();
                break;
            case "profile":
                ContentArea.Content = CreateProfileView();
                break;
            case "about":
                ContentArea.Content = CreateAboutView();
                break;
            case "logout":
                AuthManager.Instance.Logout();
                Close();
                return;
            default:
                ContentArea.Content = CreatePlaceholderView(
                    section.Substring(0, 1).ToUpper() + section[1..],
                    $"Manage your {section} settings");
                break;
        }
    }

    private void SidebarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string section)
        {
            NavigateToSection(section);
        }
    }

    private static StackPanel CreatePlaceholderView(string title, string subtitle)
    {
        return new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    Foreground = ThemeManager.Instance.TextPrimaryBrush,
                    FontSize = 24,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                new TextBlock
                {
                    Text = subtitle,
                    Foreground = ThemeManager.Instance.TextSecondaryBrush,
                    FontSize = 14
                }
            }
        };
    }

    private StackPanel CreateShortcutsView()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = "Keyboard Shortcuts",
            Foreground = ThemeManager.Instance.TextPrimaryBrush,
            FontSize = 24, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Configure which keys trigger prediction and dictation.",
            Foreground = ThemeManager.Instance.TextSecondaryBrush,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 24)
        });

        var settings = Models.SettingsManager.Instance;

        // Prediction key selector
        AddConfigurableShortcutRow(panel, "AI Prediction",
            "Tap for screenshot. Hold and speak for voice + screenshot.",
            settings.PredictionKeyCode, (keyCode) =>
            {
                settings.PredictionKeyCode = keyCode;
            });

        // Dictation key selector
        AddConfigurableShortcutRow(panel, "Voice Dictation",
            "Hold to record. Release to transcribe and paste.",
            settings.DictationKeyCode, (keyCode) =>
            {
                settings.DictationKeyCode = keyCode;
            });

        // Fixed shortcuts info
        panel.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(0, 12, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(26, 255, 255, 255))
        });

        AddShortcutRow(panel, "Click Notch", "Open Chat");
        AddShortcutRow(panel, "Escape", "Cancel current action");

        return panel;
    }

    private void AddConfigurableShortcutRow(StackPanel parent, string action, string description,
        int currentKeyCode, Action<int> onKeyChanged)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var infoPanel = new StackPanel();
        var actionText = new TextBlock
        {
            Text = action,
            Foreground = ThemeManager.Instance.TextPrimaryBrush,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var descText = new TextBlock
        {
            Text = description,
            Foreground = ThemeManager.Instance.TextSecondaryBrush,
            FontSize = 12
        };
        infoPanel.Children.Add(actionText);
        infoPanel.Children.Add(descText);
        Grid.SetColumn(infoPanel, 0);

        // Key selector dropdown
        var combo = new ComboBox
        {
            Width = 100,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(26, 255, 255, 255)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
            FontSize = 13
        };

        var selectedIndex = 0;
        var idx = 0;
        foreach (var kv in Managers.KeyboardHookManager.AvailableKeys)
        {
            combo.Items.Add(kv.Key);
            if (kv.Value == currentKeyCode) selectedIndex = idx;
            idx++;
        }
        combo.SelectedIndex = selectedIndex;

        combo.SelectionChanged += (s, e) =>
        {
            if (combo.SelectedItem is string keyName)
            {
                if (Managers.KeyboardHookManager.AvailableKeys.TryGetValue(keyName, out var keyCode))
                {
                    onKeyChanged(keyCode);
                }
            }
        };

        Grid.SetColumn(combo, 1);

        row.Children.Add(infoPanel);
        row.Children.Add(combo);
        parent.Children.Add(row);
    }

    private static void AddShortcutRow(StackPanel parent, string action, string shortcut)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var actionText = new TextBlock
        {
            Text = action,
            Foreground = ThemeManager.Instance.TextPrimaryBrush,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(actionText, 0);

        var border = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(26, 255, 255, 255)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4)
        };
        var shortcutText = new TextBlock
        {
            Text = shortcut,
            Foreground = ThemeManager.Instance.TextSecondaryBrush,
            FontSize = 12,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        };
        border.Child = shortcutText;
        Grid.SetColumn(border, 1);

        row.Children.Add(actionText);
        row.Children.Add(border);
        parent.Children.Add(row);
    }

    private StackPanel CreateProfileView()
    {
        var storage = AuthManager.Instance.Storage;
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "My Profile",
            Foreground = ThemeManager.Instance.TextPrimaryBrush,
            FontSize = 24, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 24)
        });

        AddProfileRow(panel, "Email", storage.UserEmail ?? "Not set");
        AddProfileRow(panel, "Workspace", storage.WorkspaceId ?? "Not set");
        AddProfileRow(panel, "Region", storage.Region ?? "us-east-1");
        AddProfileRow(panel, "Tenant", storage.TenantId ?? "Not set");

        return panel;
    }

    private static void AddProfileRow(StackPanel parent, string label, string value)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ThemeManager.Instance.TextSecondaryBrush,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = ThemeManager.Instance.TextPrimaryBrush,
            FontSize = 14
        });
        parent.Children.Add(row);
    }

    private StackPanel CreateAboutView()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "About VE",
            Foreground = ThemeManager.Instance.TextPrimaryBrush,
            FontSize = 24, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 24)
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"Version {Helpers.UpdateService.Instance.CurrentVersion}",
            Foreground = ThemeManager.Instance.TextPrimaryBrush,
            FontSize = 14, Margin = new Thickness(0, 0, 0, 8)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "AI-powered desktop assistant with prediction and voice dictation.",
            Foreground = ThemeManager.Instance.TextSecondaryBrush,
            FontSize = 13, Margin = new Thickness(0, 0, 0, 24)
        });

        var updateBtn = new Button
        {
            Content = "Check for Updates",
            Style = (Style)FindResource("VEButton"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        updateBtn.Click += async (s, e) =>
        {
            await Helpers.UpdateService.Instance.CheckForUpdates();
            if (Helpers.UpdateService.Instance.IsUpdateAvailable)
            {
                MessageBox.Show($"Update {Helpers.UpdateService.Instance.LatestVersion} is available!",
                    "Update Available", MessageBoxButton.OK);
            }
            else
            {
                MessageBox.Show("You're up to date!", "No Updates", MessageBoxButton.OK);
            }
        };
        panel.Children.Add(updateBtn);

        return panel;
    }
}
