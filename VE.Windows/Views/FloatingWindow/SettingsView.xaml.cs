using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class SettingsView : UserControl
{
    public event Action? BackToHome;

    private Button[] _navButtons = Array.Empty<Button>();
    private readonly ObservableCollection<TeamMember> _teamMembers = new();
    private bool _teamLoaded;
    private int _totalSeats;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible)
            {
                LoadProfileData();
                LoadWorkspaceData();
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _navButtons = new[]
        {
            NavProfile, NavWorkspace, NavTeam, NavConnectors,
            NavSubscription, NavRefer, NavAbout, NavHelp, NavTerms, NavPrivacy
        };

        TeamMembersList.ItemsSource = _teamMembers;
        LoadProfileData();
        LoadWorkspaceData();
    }

    // ═══ PROFILE ═══

    private void LoadProfileData()
    {
        var storage = AuthManager.Instance.Storage;

        NameField.Text = storage.UserName ?? "";
        EmailField.Text = storage.UserEmail ?? "";
        PhoneField.Text = "";
        ProfessionField.Text = "";

        var initials = GetInitials(storage.UserName ?? storage.UserEmail ?? "?");
        AvatarInitials.Text = initials;

        var workspace = storage.TenantName ?? "workspace";
        ProfessionLabel.Text = $"Your Profession In {workspace}";
    }

    // ═══ WORKSPACE ═══

    private void LoadWorkspaceData()
    {
        var storage = AuthManager.Instance.Storage;

        BusinessNameField.Text = storage.TenantName ?? "";
        CompanyEmailField.Text = storage.TenantEmail ?? "";
        WebsiteField.Text = storage.TenantWebsite ?? "";
        CompanyTypeField.Text = storage.TenantCompanyType ?? "";
        WorkspacePhoneField.Text = storage.TenantPhone ?? "";
        AddressField.Text = storage.TenantAddress ?? "";
    }

    // ═══ TEAM MEMBERS ═══

    private async Task LoadTeamMembers()
    {
        if (_teamLoaded) return;

        TeamLoadingText.Visibility = Visibility.Visible;

        try
        {
            var members = await TeamMembersService.Instance.GetTeamMembers();
            if (members != null)
            {
                _teamMembers.Clear();
                foreach (var m in members)
                    _teamMembers.Add(m);

                _totalSeats = 39; // Default max seats
                var count = members.Count;
                TeamTitle.Text = $"Manage Your Team Members Access  {count} / {_totalSeats}";
            }
            _teamLoaded = true;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("SettingsView", $"Load team members failed: {ex.Message}");
        }
        finally
        {
            TeamLoadingText.Visibility = Visibility.Collapsed;
        }
    }

    private async void InviteEmail_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var email = InviteEmailField.Text?.Trim();
        if (string.IsNullOrEmpty(email)) return;

        var success = await TeamMembersService.Instance.InviteMember(email);
        if (success)
        {
            InviteEmailField.Text = "";
            _teamLoaded = false;
            await LoadTeamMembers();
        }
    }

    // ═══ HELPERS ═══

    private static string GetInitials(string name)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..1].ToUpper();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
    }

    // ═══ NAVIGATION ═══

    private void BackToHome_Click(object sender, RoutedEventArgs e)
    {
        BackToHome?.Invoke();
    }

    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string section) return;

        UpdateNavSelection(section);
        ShowSection(section);
    }

    // ═══ SUBSCRIPTION ═══

    private bool _subscriptionLoaded;

    private void LoadSubscriptionData()
    {
        if (_subscriptionLoaded) return;

        var storage = AuthManager.Instance.Storage;
        var plan = storage.TenantPlan ?? "Free";
        PlanNameText.Text = plan;

        // Calculate expiry (use subscription info if available)
        PlanExpiryText.Text = "";

        // Team seats usage
        var used = _teamMembers.Count > 0 ? _teamMembers.Count : storage.TenantMemberCount;
        var total = _totalSeats > 0 ? _totalSeats : 39;
        SeatsUsedText.Text = used.ToString();
        SeatsTotalText.Text = $" used / {total}";

        // Progress bar — update after layout
        Dispatcher.BeginInvoke(() =>
        {
            var ratio = total > 0 ? (double)used / total : 0;
            var parent = SeatsProgressBar.Parent as Grid;
            if (parent != null && parent.ActualWidth > 0)
                SeatsProgressBar.Width = ratio * parent.ActualWidth;
        }, System.Windows.Threading.DispatcherPriority.Loaded);

        if (used >= total)
            SeatsStatusText.Text = "Reached the limit";
        else
            SeatsStatusText.Text = $"{total - used} seats remaining";

        _subscriptionLoaded = true;
    }

    private void ShowSection(string section)
    {
        ProfileContent.Visibility = Visibility.Collapsed;
        WorkspaceContent.Visibility = Visibility.Collapsed;
        TeamContent.Visibility = Visibility.Collapsed;
        SubscriptionContent.Visibility = Visibility.Collapsed;
        PlaceholderContent.Visibility = Visibility.Collapsed;

        switch (section)
        {
            case "profile":
                ProfileContent.Visibility = Visibility.Visible;
                break;
            case "workspace":
                WorkspaceContent.Visibility = Visibility.Visible;
                break;
            case "team":
                TeamContent.Visibility = Visibility.Visible;
                _ = LoadTeamMembers();
                break;
            case "subscription":
                SubscriptionContent.Visibility = Visibility.Visible;
                LoadSubscriptionData();
                break;
            default:
                PlaceholderContent.Visibility = Visibility.Visible;
                var titles = new Dictionary<string, string>
                {
                    ["connectors"] = "Workspace Connectors",
                    ["refer"] = "Refer & Earn",
                    ["about"] = "About",
                    ["help"] = "Help Center",
                    ["terms"] = "Terms of Use",
                    ["privacy"] = "Privacy Policy"
                };
                PlaceholderTitle.Text = titles.GetValueOrDefault(section, section);
                break;
        }
    }

    private void UpdateNavSelection(string selected)
    {
        var activeBrush = FindResource("ThemeTextPrimary") as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.White;
        var inactiveBrush = FindResource("ThemeTextTertiary") as System.Windows.Media.Brush
            ?? new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A0A4A8"));

        foreach (var btn in _navButtons)
        {
            var tag = btn.Tag as string;
            var isActive = tag == selected;
            btn.Foreground = isActive ? activeBrush : inactiveBrush;
            btn.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}
