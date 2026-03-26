using System.ComponentModel;
using System.Runtime.CompilerServices;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private string _selectedSection = "home";
    private UserProfile? _profile;
    private SubscriptionInfo? _subscription;
    private bool _isLoading;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SelectedSection
    {
        get => _selectedSection;
        set { _selectedSection = value; OnPropertyChanged(); }
    }

    public UserProfile? Profile
    {
        get => _profile;
        set { _profile = value; OnPropertyChanged(); }
    }

    public SubscriptionInfo? Subscription
    {
        get => _subscription;
        set { _subscription = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    // Settings properties bound to storage
    public SettingsManager Settings => SettingsManager.Instance;

    public async Task LoadProfileData()
    {
        IsLoading = true;
        try
        {
            Subscription = await SubscriptionService.Instance.GetSubscription();
        }
        catch (Exception ex)
        {
            Helpers.FileLogger.Instance.Warning("SettingsVM", $"Load subscription failed: {ex.Message}");
        }
        IsLoading = false;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
