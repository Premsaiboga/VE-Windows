using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private int _recentChatsPage = 1;
    private bool _hasMoreChats;
    private bool _isLoadingMoreChats;

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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateUserInfo();
        _ = LoadRecentChats();
        InitializeThemeButtons();
    }

    private void InitializeThemeButtons()
    {
        var current = VE.Windows.Theme.ThemeManager.Instance.CurrentTheme;
        var theme = current switch
        {
            Models.ThemePreference.Light => "light",
            Models.ThemePreference.Dark => "dark",
            _ => "system"
        };

        var blue = BlueBrush;
        var transparent = System.Windows.Media.Brushes.Transparent;
        var white = System.Windows.Media.Brushes.White;
        var inactive = ThemeTextTertiaryBrush;

        ThemeSystem.Background = theme == "system" ? blue : transparent;
        ThemeSystem.Foreground = theme == "system" ? white : inactive;
        ThemeLight.Background = theme == "light" ? blue : transparent;
        ThemeLight.Foreground = theme == "light" ? white : inactive;
        ThemeDark.Background = theme == "dark" ? blue : transparent;
        ThemeDark.Foreground = theme == "dark" ? white : inactive;
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
            _recentChatsPage = 1;
            var (items, hasMore) = await ChatService.Instance.ListRecentSessions(1, 20);
            _recentChats.Clear();
            foreach (var item in items)
                _recentChats.Add(item);
            _hasMoreChats = hasMore;
            _recentChatsLoaded = true;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatView", $"Load recent chats failed: {ex.Message}");
        }
    }

    private async Task LoadMoreRecentChats()
    {
        if (_isLoadingMoreChats || !_hasMoreChats) return;
        _isLoadingMoreChats = true;
        LoadingMoreText.Visibility = Visibility.Visible;

        try
        {
            _recentChatsPage++;
            var (items, hasMore) = await ChatService.Instance.ListRecentSessions(_recentChatsPage, 20);
            foreach (var item in items)
                _recentChats.Add(item);
            _hasMoreChats = hasMore;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatView", $"Load more chats failed: {ex.Message}");
        }
        finally
        {
            _isLoadingMoreChats = false;
            LoadingMoreText.Visibility = Visibility.Collapsed;
        }
    }

    private void RecentChatsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv &&
            sv.VerticalOffset >= sv.ScrollableHeight - 10 &&
            sv.ScrollableHeight > 0)
        {
            _ = LoadMoreRecentChats();
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
            ToggleChat.Foreground = ThemeTextTertiaryBrush;

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

    private bool _isLoadingChat;

    private async void RecentChat_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoadingChat) return;
        if (sender is Button btn && btn.Tag is string sessionId)
        {
            FileLogger.Instance.Debug("ChatView", $"Loading chat: {sessionId}");
            _isLoadingChat = true;

            // Clear current chat and show loading state
            _vm.ClearHistory();
            WelcomePanel.Visibility = Visibility.Collapsed;
            MessagesScroll.Visibility = Visibility.Visible;

            // Set session ID so future messages continue this conversation
            ChatManager.Instance.SetSessionId(sessionId);

            try
            {
                var messages = await ChatService.Instance.GetSessionMessages(sessionId, 1, 50);
                if (messages.Count == 0)
                {
                    // Show welcome panel if no messages found
                    WelcomePanel.Visibility = Visibility.Visible;
                    MessagesScroll.Visibility = Visibility.Collapsed;
                    _isLoadingChat = false;
                    return;
                }

                // Messages come in reverse order (newest first), so reverse them
                messages.Reverse();

                foreach (var msg in messages)
                {
                    if (!string.IsNullOrEmpty(msg.Query))
                    {
                        ChatManager.Instance.Messages.Add(new ChatMessage
                        {
                            Role = ChatRole.User,
                            Content = msg.Query
                        });
                    }
                    if (!string.IsNullOrEmpty(msg.Response))
                    {
                        ChatManager.Instance.Messages.Add(new ChatMessage
                        {
                            Role = ChatRole.Assistant,
                            Content = msg.Response
                        });
                    }
                }

                // Scroll to bottom
                Dispatcher.BeginInvoke(() =>
                {
                    if (MessagesScroll.ScrollableHeight > 0)
                        MessagesScroll.ScrollToEnd();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("ChatView", $"Load chat history failed: {ex.Message}");
                // Show welcome panel on error so user isn't stuck on blank screen
                WelcomePanel.Visibility = Visibility.Visible;
                MessagesScroll.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _isLoadingChat = false;
            }
        }
    }

    // ═══ CHAT / WORK TOGGLE ═══

    private static System.Windows.Media.SolidColorBrush BlueBrush =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007CEC"));

    private static System.Windows.Media.Brush ThemeTextPrimaryBrush =>
        Application.Current.Resources["ThemeTextPrimary"] as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
    private static System.Windows.Media.Brush ThemeTextTertiaryBrush =>
        Application.Current.Resources["ThemeTextTertiary"] as System.Windows.Media.Brush
        ?? new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#878E92"));
    private static System.Windows.Media.Brush ThemeTextSecondaryBrush2 =>
        Application.Current.Resources["ThemeTextSecondary"] as System.Windows.Media.Brush
        ?? new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#878E92"));

    private void ToggleChat_Click(object sender, RoutedEventArgs e)
    {
        if (_isChatMode) return;
        _isChatMode = true;
        ToggleChat.Background = BlueBrush;
        ToggleChat.Foreground = System.Windows.Media.Brushes.White;
        ToggleWork.Background = System.Windows.Media.Brushes.Transparent;
        ToggleWork.Foreground = ThemeTextTertiaryBrush;

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
        ToggleChat.Foreground = ThemeTextTertiaryBrush;

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
                "Shared" => new SharedWithMeView(),
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
                Foreground = ThemeTextSecondaryBrush2,
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
            btn.Foreground = isActive ? ThemeTextPrimaryBrush : ThemeTextTertiaryBrush;
            btn.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // ═══ SUGGESTION ═══

    private async void Suggestion_Click(object sender, MouseButtonEventArgs e)
    {
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
        CloseInputPopups();
        var text = ChatInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _vm.CurrentInput = text;
        ChatInput.Text = "";
        await _vm.SendMessage();
    }

    // ═══ CLOSE POPUPS HELPER ═══

    private void CloseInputPopups()
    {
        PlusMenuPopup.Visibility = Visibility.Collapsed;
        ModelSelectorPopup.Visibility = Visibility.Collapsed;
    }

    // ═══ PLUS MENU & MODEL SELECTOR ═══

    private bool _searchWebEnabled = true;
    private bool _internalKnowledgeEnabled = true;
    private bool _deepSearchEnabled;

    private void PlusButton_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ModelSelectorPopup.Visibility = Visibility.Collapsed;
        PlusMenuPopup.Visibility = PlusMenuPopup.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ModelSelector_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        PlusMenuPopup.Visibility = Visibility.Collapsed;
        RefreshModelList();
        ModelSelectorPopup.Visibility = Visibility.Visible;
    }

    private void RefreshModelList()
    {
        var current = ChatManager.Instance.SelectedModel;
        var items = AIModel.AvailableModels.Select(m => new ModelDisplayItem
        {
            Id = m.Id,
            DisplayName = m.DisplayName,
            Icon = m.Id == "auto" ? "ve" : "&#x25CF;",
            IsSelected = m.Id == current.Id,
            IsDefault = m.Id == "auto"
        }).ToList();
        ModelList.ItemsSource = items;
    }

    private void ModelItem_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement el && el.Tag is string modelId)
        {
            var model = AIModel.AvailableModels.FirstOrDefault(m => m.Id == modelId) ?? AIModel.Auto;
            ChatManager.Instance.SelectedModel = model;
            SelectedModelName.Text = model.Id == "auto" ? "Ve" : model.DisplayName;
            ModelSelectorPopup.Visibility = Visibility.Collapsed;
            PlusMenuPopup.Visibility = Visibility.Collapsed;
        }
    }

    private void ToggleSearchWeb_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _searchWebEnabled = !_searchWebEnabled;
        ToggleSearchWeb.Background = _searchWebEnabled ? BlueBrush : new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555"));
        SearchWebKnob.HorizontalAlignment = _searchWebEnabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    private void ToggleInternalKnowledge_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _internalKnowledgeEnabled = !_internalKnowledgeEnabled;
        ToggleInternalKnowledge.Background = _internalKnowledgeEnabled ? BlueBrush : new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555"));
        InternalKnowledgeKnob.HorizontalAlignment = _internalKnowledgeEnabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    private void ToggleDeepSearch_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _deepSearchEnabled = !_deepSearchEnabled;
        ToggleDeepSearch.Background = _deepSearchEnabled ? BlueBrush : new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555"));
        DeepSearchKnob.HorizontalAlignment = _deepSearchEnabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    // ═══ ACCOUNT POPUP ═══

    private void UserProfile_Click(object sender, MouseButtonEventArgs e)
    {
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
        AccountOverlay.Visibility = Visibility.Collapsed;
    }

    // ═══ THEME TOGGLE ═══

    private void ThemeSystem_Click(object sender, RoutedEventArgs e) => SetThemeButton("system");
    private void ThemeLight_Click(object sender, RoutedEventArgs e) => SetThemeButton("light");
    private void ThemeDark_Click(object sender, RoutedEventArgs e) => SetThemeButton("dark");

    private void SetThemeButton(string theme)
    {
        var blue = BlueBrush;
        var transparent = System.Windows.Media.Brushes.Transparent;
        var white = System.Windows.Media.Brushes.White;
        var inactive = ThemeTextTertiaryBrush;

        ThemeSystem.Background = theme == "system" ? blue : transparent;
        ThemeSystem.Foreground = theme == "system" ? white : inactive;
        ThemeLight.Background = theme == "light" ? blue : transparent;
        ThemeLight.Foreground = theme == "light" ? white : inactive;
        ThemeDark.Background = theme == "dark" ? blue : transparent;
        ThemeDark.Foreground = theme == "dark" ? white : inactive;

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

public class ModelDisplayItem
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsSelected { get; set; }
    public bool IsDefault { get; set; }
}
