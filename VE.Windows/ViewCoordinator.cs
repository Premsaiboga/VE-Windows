using System.ComponentModel;
using System.Runtime.CompilerServices;
using VE.Windows.Services;

namespace VE.Windows;

public enum CombinedPredictionState { Inactive, Waiting, Streaming, Success, Error }
public enum NavigationTab { Home, Chat, Notes, Connectors, Knowledge, Instructions, Memory, Voice, Shortcuts }
public enum RestrictedState { None, NoInternet, Expired, Waitlist, Suspended }

/// <summary>
/// View state coordination - single source of truth for UI state.
/// Equivalent to macOS VEAIViewCoordinator.
/// </summary>
public sealed class ViewCoordinator : INotifyPropertyChanged
{
    public static ViewCoordinator Instance { get; } = new();

    private CombinedPredictionState _combinedPredictionState = CombinedPredictionState.Inactive;
    private DictationState _dictationState = DictationState.Inactive;
    private MeetingState _meetingState = MeetingState.Inactive;
    private bool _isRecording;
    private bool _hasError;
    private RestrictedState _restrictedState = RestrictedState.None;
    private NavigationTab _selectedNavigationTab = NavigationTab.Home;
    private bool _shouldHideNotchBar;
    private bool _isUpdateBannerVisible;
    private string? _predictionText;
    private string? _errorMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CombinedPredictionState CombinedPredictionState
    {
        get => _combinedPredictionState;
        set { _combinedPredictionState = value; OnPropertyChanged(); }
    }

    public DictationState DictationState
    {
        get => _dictationState;
        set { _dictationState = value; OnPropertyChanged(); }
    }

    public MeetingState MeetingState
    {
        get => _meetingState;
        set { _meetingState = value; OnPropertyChanged(); }
    }

    public bool IsRecording
    {
        get => _isRecording;
        set { _isRecording = value; OnPropertyChanged(); }
    }

    public bool HasError
    {
        get => _hasError;
        set { _hasError = value; OnPropertyChanged(); }
    }

    public RestrictedState RestrictedState
    {
        get => _restrictedState;
        set { _restrictedState = value; OnPropertyChanged(); }
    }

    public NavigationTab SelectedNavigationTab
    {
        get => _selectedNavigationTab;
        set { _selectedNavigationTab = value; OnPropertyChanged(); }
    }

    public bool ShouldHideNotchBar
    {
        get => _shouldHideNotchBar;
        set { _shouldHideNotchBar = value; OnPropertyChanged(); }
    }

    public bool IsUpdateBannerVisible
    {
        get => _isUpdateBannerVisible;
        set { _isUpdateBannerVisible = value; OnPropertyChanged(); }
    }

    public string? PredictionText
    {
        get => _predictionText;
        set { _predictionText = value; OnPropertyChanged(); }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    // Permission HUD
    public bool ShowPermissionHUD { get; set; }
    public string? PermissionHUDType { get; set; }

    private ViewCoordinator()
    {
        // Monitor network
        Helpers.NetworkMonitor.Instance.NetworkChanged += (s, connected) =>
        {
            RestrictedState = connected ? RestrictedState.None : RestrictedState.NoInternet;
        };

        if (!Helpers.NetworkMonitor.Instance.IsConnected)
        {
            RestrictedState = RestrictedState.NoInternet;
        }
    }

    public void ResetAllStates()
    {
        CombinedPredictionState = CombinedPredictionState.Inactive;
        DictationState = DictationState.Inactive;
        MeetingState = MeetingState.Inactive;
        IsRecording = false;
        HasError = false;
        PredictionText = null;
        ErrorMessage = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
