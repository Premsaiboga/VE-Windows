using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.WebSockets;
using VE.Windows.Managers;
using VE.Windows.Models;
using VE.Windows.Services;
using VE.Windows.WebSocket;

namespace VE.Windows.Infrastructure;

/// <summary>
/// Service interfaces for dependency injection and testability.
/// Each interface mirrors the public surface of its corresponding singleton.
/// </summary>

public interface IAuthManager : INotifyPropertyChanged
{
    bool IsAuthenticated { get; }
    AuthState AuthState { get; }
    AuthStorage Storage { get; }
    Task HandleOAuthCallback(string sessionId);
    void Logout();
}

public interface INetworkService
{
    Task<T?> GetAsync<T>(string url) where T : class;
    Task<T?> PostAsync<T>(string url, object? body = null) where T : class;
    Task<T?> PutAsync<T>(string url, object? body = null) where T : class;
    Task<T?> PatchAsync<T>(string url, object? body = null) where T : class;
    Task<bool> DeleteAsync(string url);
    Task<string?> GetRawAsync(string url);
    Task<Result<string>> PostRawCheckedAsync(string url, object? body = null);
    Task<string?> PostRawAsync(string url, object? body = null);
    Task<string?> PutRawAsync(string url, object? body = null);
    Task<string?> DeleteRawAsync(string url, object? body = null);
    void ScopeCookiesToDomain(Uri baseUri);
    int GetCookieCount(Uri uri);
    void PersistCookies(Uri uri);
    void ClearPersistedCookies();
}

public interface IWebSocketRegistry : IDisposable
{
    WebSocket.UnifiedAudioSocketClient? UnifiedAudioClient { get; }
    WebSocket.MeetingSocketClient? MeetingClient { get; }
    WebSocket.SummarySocketClient? SummaryClient { get; }

    Task ConnectUnifiedAudioTransport();
    Task ConnectMeetingTransport(string meetingId);
    Task ConnectSummaryTransport(string meetingId);
    Task DisconnectMeetingTransport();
    Task DisconnectSummaryTransport();
    Task DisconnectAll();
    Task OnAppActivated();
    void OnAppDeactivated();
}

public interface IAudioService : INotifyPropertyChanged
{
    bool IsRecording { get; }
    event EventHandler<byte[]>? OnAudioDataAvailable;
    void StartCapture();
    void StopCapture();
    void RefreshDevices();
}

public interface IScreenCaptureManager
{
    string GetActiveAppName();
    string GetActiveWindowTitle();
    IntPtr GetForegroundWindowHandle();
    byte[]? CaptureActiveWindow();
}

public interface IClipboardManager
{
    void PasteText(string text);
    void PasteTextToWindow(string text, IntPtr windowHandle);
}

public interface IViewCoordinator : INotifyPropertyChanged
{
    CombinedPredictionState CombinedPredictionState { get; set; }
    DictationState DictationState { get; set; }
    MeetingState MeetingState { get; set; }
    bool IsRecording { get; set; }
    bool HasError { get; set; }
    RestrictedState RestrictedState { get; set; }
    NavigationTab SelectedNavigationTab { get; set; }
    bool IsFloatingPanelVisible { get; set; }
    bool IsAppActive { get; set; }
    string? PredictionText { get; set; }
    string? ErrorMessage { get; set; }
    string? ErrorCapsuleMessage { get; set; }
    bool IsAnyOperationActive { get; }
    bool IsMeetingDetected { get; set; }
    string? DetectedMeetingAppName { get; set; }
    void ResetAllStates();
    void ShowErrorCapsule(string message, int dismissAfterMs = 5000);
}

public interface ISettingsManager
{
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
    bool Has(string key);
    void Remove(string key);

    // Typed properties
    int PredictionKeyCode { get; }
    int DictationKeyCode { get; }
    string? SelectedMicrophoneUID { get; set; }
    string? SelectedMicrophoneName { get; set; }
    bool PreferBuiltInOverBluetooth { get; set; }
    bool EnableMeetingScreenAudio { get; set; }
    ThemePreference ThemePreference { get; set; }
}

public interface IFileLogger
{
    void Debug(string category, string message);
    void Info(string category, string message);
    void Warning(string category, string message);
    void Error(string category, string message);
    void Critical(string category, string message);
    void FlushNow();
}

public interface IErrorService
{
    void ConfigureSentry();
    void UpdateSentryUser();
    void LogMessage(string message, ErrorCategory category, ErrorSeverity severity, Dictionary<string, string>? context = null);
    void LogException(Exception exception, ErrorCategory category, Dictionary<string, string>? context = null);
    void LogCrashAndReport(Exception? exception);
    Task Flush();
}

public interface ITokenRefreshService : IDisposable
{
    Task<bool> EnsureTokenRefreshed();
    void ScheduleRefresh(string token);
}

public interface IDictationService : INotifyPropertyChanged
{
    DictationState State { get; }
    string TranscribedText { get; }
    string? ErrorMessage { get; }
    IntPtr TargetWindowHandle { get; set; }

    event EventHandler<string>? OnDictationError;
    event EventHandler? OnDictationStarted;
    event EventHandler? OnDictationProcessing;
    event EventHandler<string>? OnDictationSuccess;

    Task StartDictation();
    Task StopDictation();
    void CancelDictation();
}

public interface IMeetingService : INotifyPropertyChanged
{
    MeetingState State { get; }
    bool IsActive { get; }
    ObservableCollection<MeetingTranscription> LiveTranscriptions { get; }
    string? CurrentMeetingId { get; }
    TimeSpan Duration { get; }
    bool IsSummaryGenerationInProgress { get; }

    event EventHandler? MeetingListNeedsRefresh;
    event EventHandler? InactivityAlertTriggered;
    event EventHandler<MeetingAnalyticsData>? SummaryGenerationComplete;
    event EventHandler? SummaryGenerationSkipped;

    Task StartMeeting(string? title = null);
    Task StopMeeting();
    void PauseMeeting();
    void ResumeMeeting();
    void DismissResult();
    void ContinueFromInactivityAlert();
    Task EndMeetingFromInactivityAlert();
    Task<bool> TriggerSummaryGeneration(string meetingId);
}

public interface IBaseURLService
{
    string? GetBaseUrl(string type, string? region = null);
    string? GetGlobalUrl(string type);
}
