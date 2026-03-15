using System.ComponentModel;
using System.Runtime.CompilerServices;
using VE.Windows.Helpers;
using VE.Windows.Services;

namespace VE.Windows;

public enum CombinedPredictionState { Inactive, Waiting, Streaming, Success, Error }
public enum NavigationTab { Home, Chat, Notes, Connectors, Knowledge, Instructions, Memory, Voice, Shortcuts }
public enum RestrictedState { None, NoInternet, Expired, Waitlist, Suspended }
public enum UpdateState { None, Available, Downloading, ReadyToInstall }

/// <summary>
/// View state coordination - single source of truth for UI state.
/// Equivalent to macOS VEAIViewCoordinator.
/// All property setters dispatch to the UI thread via DispatcherHelper.
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
    private UpdateState _updateState = UpdateState.None;
    private string? _predictionText;
    private string? _errorMessage;
    private string? _errorCapsuleMessage;
    private bool _isFloatingPanelVisible;
    private bool _isAppActive = true;

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

    public UpdateState UpdateState
    {
        get => _updateState;
        set { _updateState = value; OnPropertyChanged(); }
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

    /// <summary>
    /// Error capsule message (shown below notch, auto-dismisses).
    /// </summary>
    public string? ErrorCapsuleMessage
    {
        get => _errorCapsuleMessage;
        set { _errorCapsuleMessage = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether the floating settings/chat panel is visible.
    /// </summary>
    public bool IsFloatingPanelVisible
    {
        get => _isFloatingPanelVisible;
        set { _isFloatingPanelVisible = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether the app is in the foreground (active).
    /// Used by WebSocketRegistry for lifecycle management.
    /// </summary>
    public bool IsAppActive
    {
        get => _isAppActive;
        set { _isAppActive = value; OnPropertyChanged(); }
    }

    // Permission HUD
    private bool _showPermissionHUD;
    private string? _permissionHUDType;

    public bool ShowPermissionHUD
    {
        get => _showPermissionHUD;
        set { _showPermissionHUD = value; OnPropertyChanged(); }
    }

    public string? PermissionHUDType
    {
        get => _permissionHUDType;
        set { _permissionHUDType = value; OnPropertyChanged(); }
    }

    // Meeting detection (populated by MeetingDetectionService)
    private bool _isMeetingDetected;
    private string? _detectedMeetingAppName;

    public bool IsMeetingDetected
    {
        get => _isMeetingDetected;
        set { _isMeetingDetected = value; OnPropertyChanged(); }
    }

    public string? DetectedMeetingAppName
    {
        get => _detectedMeetingAppName;
        set { _detectedMeetingAppName = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether any active operation is in progress (prediction, dictation, or meeting).
    /// Useful for UI to show/hide global activity indicators.
    /// </summary>
    public bool IsAnyOperationActive =>
        CombinedPredictionState != CombinedPredictionState.Inactive ||
        DictationState != DictationState.Inactive ||
        MeetingState != MeetingState.Inactive;

    private ViewCoordinator()
    {
        // Monitor network
        NetworkMonitor.Instance.NetworkChanged += (s, connected) =>
        {
            DispatcherHelper.PostOnUI(() =>
            {
                RestrictedState = connected ? RestrictedState.None : RestrictedState.NoInternet;
            });
        };

        if (!NetworkMonitor.Instance.IsConnected)
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
        ErrorCapsuleMessage = null;
    }

    /// <summary>
    /// Show a temporary error capsule below the notch. Auto-dismisses after the specified duration.
    /// Thread-safe: can be called from any thread.
    /// </summary>
    public void ShowErrorCapsule(string message, int dismissAfterMs = 5000)
    {
        DispatcherHelper.PostOnUI(() =>
        {
            ErrorCapsuleMessage = message;
            HasError = true;
        });

        // Auto-dismiss
        _ = Task.Delay(dismissAfterMs).ContinueWith(_ =>
        {
            DispatcherHelper.PostOnUI(() =>
            {
                if (ErrorCapsuleMessage == message)
                {
                    ErrorCapsuleMessage = null;
                    HasError = false;
                }
            });
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        DispatcherHelper.PostOnUI(() =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        });
    }
}
