using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Models;
using VE.Windows.Services;
using VE.Windows.ViewModels;

namespace VE.Windows.Views.FloatingWindow;

public partial class ChatView : UserControl
{
    private readonly ChatViewModel _vm = new();
    private readonly ObservableCollection<RecentChatItem> _recentChats = new();
    private readonly ObservableCollection<WorkspaceDisplayItem> _workspaces = new();
    private bool _isChatMode = true;
    private bool _recentChatsLoaded;

    /// <summary>
    /// Raised when user clicks Integrations or other sidebar actions that need tab switching.
    /// </summary>
    public event Action<string>? NavigateToTab;

    public ChatView()
    {
        InitializeComponent();
        DataContext = _vm;
        MessagesList.ItemsSource = _vm.Messages;
        RecentChatsList.ItemsSource = _recentChats;
        WorkspacesList.ItemsSource = _workspaces;

        _vm.Messages.CollectionChanged += (s, e) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                WelcomePanel.Visibility = _vm.HasMessages ? Visibility.Collapsed : Visibility.Visible;
                MessagesScroll.Visibility = _vm.HasMessages ? Visibility.Visible : Visibility.Collapsed;

                if (MessagesScroll.ScrollableHeight > 0)
                    MessagesScroll.ScrollToEnd();
            });
        };

        Loaded += OnLoaded;
        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible && !_recentChatsLoaded)
                _ = LoadRecentChats();
        };

        // Subscribe to theme changes
        Theme.ThemeManager.Instance.ThemeChanged += (s, e) =>
            Dispatcher.BeginInvoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        var tm = Theme.ThemeManager.Instance;
        var isDark = tm.IsDarkMode;

        var bg = isDark ? BrushFrom("#1A1A1A") : BrushFrom("#F4F5F5");
        var sidebar = isDark ? BrushFrom("#111315") : BrushFrom("#E8E9EA");
        var card = isDark ? BrushFrom("#25292D") : BrushFrom("#FFFFFF");
        var textPrimary = isDark ? BrushFrom("#F4F5F5") : BrushFrom("#272B30");
        var border = isDark ? BrushFrom("#1AFFFFFF") : BrushFrom("#1A000000");

        // Main backgrounds
        RootGrid.Background = bg;
        SidebarBorder.Background = sidebar;
        SidebarBorder.BorderBrush = border;

        // Toggle pill
        TogglePill.Background = card;

        // Input bar
        InputBarInner.Background = card;
        InputBarInner.BorderBrush = border;
        ChatInput.Foreground = textPrimary;

        // Welcome text
        GreetingName.Foreground = textPrimary;

        // User profile section
        UserWorkspaceText.Foreground = textPrimary;

        // Account popup
        AccountPopup.Background = isDark ? BrushFrom("#1E2024") : BrushFrom("#FFFFFF");
        AccountPopup.BorderBrush = border;

        // Update parent window border
        if (Window.GetWindow(this) is FloatingPanelWindow fpw)
        {
            fpw.WindowBorder.Background = bg;
            fpw.WindowBorder.BorderBrush = border;
            fpw.UpdateShadowBackground(bg);
        }

        // Sync theme button selection
        var currentTheme = tm.CurrentTheme;
        SetThemeButton(currentTheme switch
        {
            ThemePreference.Light => "light",
            ThemePreference.Dark => "dark",
            _ => "system"
        });
    }

    private static SolidColorBrush BrushFrom(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateUserInfo();
        _ = LoadRecentChats();
        ApplyTheme();
    }

    // ═══ USER INFO ═══

    private void UpdateUserInfo()
    {
        var storage = AuthManager.Instance.Storage;
        var name = storage.UserName ?? storage.UserEmail ?? "User";
        var workspace = storage.TenantName ?? "";

        // Bottom profile: workspace name on top, user name below (matches macOS)
        UserWorkspaceText.Text = workspace;
        UserNameText.Text = name;
        UserInitials.Text = GetInitials(workspace.Length > 0 ? workspace : name);
        GreetingName.Text = $"Hey {name.Split(' ')[0]},";

        // Update messages visibility
        WelcomePanel.Visibility = _vm.HasMessages ? Visibility.Collapsed : Visibility.Visible;
        MessagesScroll.Visibility = _vm.HasMessages ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string GetInitials(string name)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..1].ToUpper();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
    }

    // ═══ RECENT CHATS ═══

    private async Task LoadRecentChats()
    {
        try
        {
            var (items, _) = await ChatService.Instance.ListRecentSessions(1, 5);
            _recentChats.Clear();
            foreach (var item in items)
                _recentChats.Add(item);
            _recentChatsLoaded = true;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatView", $"Load recent chats failed: {ex.Message}");
        }
    }

    // ═══ SIDEBAR ACTIONS ═══

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearHistory();
        WelcomePanel.Visibility = Visibility.Visible;
        MessagesScroll.Visibility = Visibility.Collapsed;
        ChatInput.Focus();
    }

    private void Integrations_Click(object sender, RoutedEventArgs e)
    {
        // Switch to Work mode and show Connectors
        if (_isChatMode)
        {
            _isChatMode = false;
            ToggleWork.Background = BlueBrush;
            ToggleWork.Foreground = System.Windows.Media.Brushes.White;
            ToggleChat.Background = System.Windows.Media.Brushes.Transparent;
            ToggleChat.Foreground = GrayBrush;

            ChatSidebar.Visibility = Visibility.Collapsed;
            WorkSidebar.Visibility = Visibility.Visible;

            WelcomePanel.Visibility = Visibility.Collapsed;
            MessagesScroll.Visibility = Visibility.Collapsed;
            InputBar.Visibility = Visibility.Collapsed;
            WorkContent.Visibility = Visibility.Visible;
        }

        ShowWorkView("Connectors");
        UpdateWorkNavSelection("Connectors");
    }

    private void RecentChat_Click(object sender, RoutedEventArgs e)
    {
        // Future: load selected conversation messages
        if (sender is Button btn && btn.Tag is string chatId)
        {
            FileLogger.Instance.Debug("ChatView", $"Selected recent chat: {chatId}");
        }
    }

    // ═══ CHAT / WORK TOGGLE ═══

    private static System.Windows.Media.SolidColorBrush BlueBrush =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007CEC"));
    private static System.Windows.Media.SolidColorBrush GrayBrush =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#878E92"));

    private void ToggleChat_Click(object sender, RoutedEventArgs e)
    {
        if (_isChatMode) return;
        _isChatMode = true;
        ToggleChat.Background = BlueBrush;
        ToggleChat.Foreground = System.Windows.Media.Brushes.White;
        ToggleWork.Background = System.Windows.Media.Brushes.Transparent;
        ToggleWork.Foreground = GrayBrush;

        // Swap sidebars
        ChatSidebar.Visibility = Visibility.Visible;
        WorkSidebar.Visibility = Visibility.Collapsed;

        // Show chat content
        WelcomePanel.Visibility = _vm.HasMessages ? Visibility.Collapsed : Visibility.Visible;
        MessagesScroll.Visibility = _vm.HasMessages ? Visibility.Visible : Visibility.Collapsed;
        WorkContent.Visibility = Visibility.Collapsed;
        InputBar.Visibility = Visibility.Visible;
    }

    private void ToggleWork_Click(object sender, RoutedEventArgs e)
    {
        if (!_isChatMode) return;
        _isChatMode = false;
        ToggleWork.Background = BlueBrush;
        ToggleWork.Foreground = System.Windows.Media.Brushes.White;
        ToggleChat.Background = System.Windows.Media.Brushes.Transparent;
        ToggleChat.Foreground = GrayBrush;

        // Swap sidebars
        ChatSidebar.Visibility = Visibility.Collapsed;
        WorkSidebar.Visibility = Visibility.Visible;

        // Hide chat content, show work content, hide input bar
        WelcomePanel.Visibility = Visibility.Collapsed;
        MessagesScroll.Visibility = Visibility.Collapsed;
        InputBar.Visibility = Visibility.Collapsed;
        WorkContent.Visibility = Visibility.Visible;

        // Default to Notes
        ShowWorkView("Notes");
        UpdateWorkNavSelection("Notes");
    }

    // ═══ WORK SIDEBAR NAVIGATION ═══

    private string _selectedWorkNav = "Notes";
    private readonly Dictionary<string, UserControl> _workViews = new();

    private void WorkNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tab)
        {
            _selectedWorkNav = tab;
            UpdateWorkNavSelection(tab);
            ShowWorkView(tab);
        }
    }

    private void ShowWorkView(string tab)
    {
        if (!_workViews.TryGetValue(tab, out var view))
        {
            view = tab switch
            {
                "Mail" => new MailView(),
                "Prediction" => new PredictionView2(),
                "Routines" => new RoutinesView(),
                "IntentModel" => new IntentModelView(),
                "Knowledge" => new FilesView(),
                "Connectors" => new ConnectorsView(),
                _ => null
            };
            if (view != null) _workViews[tab] = view;
        }

        if (view != null)
        {
            WorkContent.Content = view;
        }
        else
        {
            // Placeholder for unimplemented views (Notes, Routines, Shared, Shortcuts)
            WorkContent.Content = new TextBlock
            {
                Text = $"{tab}\nComing soon",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#878E92")),
                FontSize = 16,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 80, 0, 0)
            };
        }
    }

    private void UpdateWorkNavSelection(string selected)
    {
        var navButtons = new[]
        {
            WorkNavNotes, WorkNavMails, WorkNavPrediction, WorkNavRoutines,
            WorkNavIntentModel, WorkNavFiles, WorkNavShared, WorkNavConnectors, WorkNavShortcuts
        };

        foreach (var btn in navButtons)
        {
            var tag = btn.Tag as string;
            var isActive = tag == selected;
            btn.Foreground = isActive
                ? System.Windows.Media.Brushes.White
                : GrayBrush;
            btn.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // ═══ SUGGESTION ═══

    private async void Suggestion_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement el && el.Tag is string text)
        {
            _vm.CurrentInput = text;
            ChatInput.Text = "";
            await _vm.SendMessage();
        }
    }

    // ═══ SEND MESSAGE ═══

    private async void SendButton_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await SendMessage();
    }

    private async void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        var text = ChatInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _vm.CurrentInput = text;
        ChatInput.Text = "";
        await _vm.SendMessage();
    }

    // ═══ ACCOUNT POPUP ═══

    private void UserProfile_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (AccountOverlay.Visibility == Visibility.Visible)
        {
            AccountOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        _ = ShowAccountPopup();
    }

    private void SettingsGear_Click(object sender, RoutedEventArgs e)
    {
        ShowSettings();
    }

    // ═══ SETTINGS VIEW ═══

    private SettingsView? _settingsView;

    private void ShowSettings()
    {
        AccountOverlay.Visibility = Visibility.Collapsed;

        if (_settingsView == null)
        {
            _settingsView = new SettingsView();
            _settingsView.BackToHome += () =>
            {
                SettingsOverlay.Visibility = Visibility.Collapsed;
                SettingsOverlay.Content = null;
            };
        }

        SettingsOverlay.Content = _settingsView;
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private async Task ShowAccountPopup()
    {
        var storage = AuthManager.Instance.Storage;

        // Set known info immediately
        PopupWorkspaceName.Text = storage.TenantName ?? "Workspace";
        WorkspaceInitial.Text = (storage.TenantName ?? "W")[..1].ToUpper();
        PopupWorkspaceDetail.Text = storage.TenantPlan != null
            ? $"{storage.TenantPlan} Plan"
            : "";

        AccountOverlay.Visibility = Visibility.Visible;

        // Load workspaces and intent score in background
        _ = LoadWorkspaces();
        _ = LoadIntentScore();
    }

    private async Task LoadWorkspaces()
    {
        try
        {
            var workspaces = await WorkspaceService.Instance.GetWorkspaces();
            if (workspaces == null) return;

            var currentId = AuthManager.Instance.Storage.WorkspaceId;
            _workspaces.Clear();
            foreach (var ws in workspaces)
            {
                _workspaces.Add(new WorkspaceDisplayItem
                {
                    Id = ws.Id,
                    Name = ws.Name,
                    Initial = ws.Name.Length > 0 ? ws.Name[..1].ToUpper() : "?",
                    IsSelected = ws.Id == currentId,
                    MemberCount = ws.MemberCount
                });
            }

            // Update member count in popup header
            var current = _workspaces.FirstOrDefault(w => w.IsSelected);
            if (current != null && current.MemberCount > 0)
            {
                var plan = AuthManager.Instance.Storage.TenantPlan ?? "Free";
                PopupWorkspaceDetail.Text = $"{current.MemberCount} members \u2022 {plan} Plan";
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatView", $"Load workspaces failed: {ex.Message}");
        }
    }

    private async Task LoadIntentScore()
    {
        try
        {
            // Fetch raw intent score from API
            var baseUrl = BaseURLService.Instance.GetBaseUrl("whatsapp");
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return;

            var response = await NetworkService.Instance.GetRawAsync(
                $"{baseUrl}/email-tags/workspace/{workspaceId}/get-intent-score");
            if (response == null) return;

            var json = Newtonsoft.Json.Linq.JObject.Parse(response);
            var score = (int?)(json["intentScore"]?.ToObject<int>())
                        ?? (int?)(json["score"]?.ToObject<int>())
                        ?? (int?)(json["data"]?["intentScore"]?.ToObject<int>())
                        ?? (int?)(json["data"]?["score"]?.ToObject<int>());
            if (score.HasValue)
            {
                IntentScoreText.Text = $"{score.Value}%";
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Debug("ChatView", $"Load intent score: {ex.Message}");
        }
    }

    private void CloseAccountPopup_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AccountOverlay.Visibility = Visibility.Collapsed;
    }

    // ═══ THEME TOGGLE ═══

    private void ThemeSystem_Click(object sender, RoutedEventArgs e) => SetThemeButton("system");
    private void ThemeLight_Click(object sender, RoutedEventArgs e) => SetThemeButton("light");
    private void ThemeDark_Click(object sender, RoutedEventArgs e) => SetThemeButton("dark");

    private void SetThemeButton(string theme)
    {
        var blue = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007CEC"));
        var transparent = System.Windows.Media.Brushes.Transparent;
        var white = System.Windows.Media.Brushes.White;
        var gray = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#878E92"));

        ThemeSystem.Background = theme == "system" ? blue : transparent;
        ThemeSystem.Foreground = theme == "system" ? white : gray;
        ThemeLight.Background = theme == "light" ? blue : transparent;
        ThemeLight.Foreground = theme == "light" ? white : gray;
        ThemeDark.Background = theme == "dark" ? blue : transparent;
        ThemeDark.Foreground = theme == "dark" ? white : gray;

        // Apply theme via ThemeManager
        VE.Windows.Theme.ThemeManager.Instance.CurrentTheme = theme switch
        {
            "light" => Models.ThemePreference.Light,
            "dark" => Models.ThemePreference.Dark,
            _ => Models.ThemePreference.System
        };
    }

    // ═══ WORKSPACE SWITCH ═══

    private async void SwitchWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string workspaceId)
        {
            var current = AuthManager.Instance.Storage.WorkspaceId;
            if (workspaceId == current) return;

            var success = await WorkspaceService.Instance.SwitchWorkspace(workspaceId);
            if (success)
            {
                AuthManager.Instance.Storage.WorkspaceId = workspaceId;
                AccountOverlay.Visibility = Visibility.Collapsed;

                // Refresh UI
                UpdateUserInfo();
                _recentChatsLoaded = false;
                _ = LoadRecentChats();

                FileLogger.Instance.Info("ChatView", $"Switched to workspace: {workspaceId}");
            }
        }
    }

    // ═══ LOGOUT ═══

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        AccountOverlay.Visibility = Visibility.Collapsed;
        AuthManager.Instance.Logout();
    }
}

/// <summary>
/// Display model for workspace list in account popup.
/// </summary>
public class WorkspaceDisplayItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initial { get; set; } = "";
    public bool IsSelected { get; set; }
    public int MemberCount { get; set; }
}
