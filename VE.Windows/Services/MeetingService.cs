using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Models;
using VE.Windows.WebSocket;

namespace VE.Windows.Services;

public enum MeetingState
{
    Inactive,
    Starting,
    Active,
    Paused,
    Result,
    Error
}

/// <summary>
/// Meeting service that matches macOS MeetingService.
/// Uses GraphQL to create meeting, WebSocket for audio streaming and transcriptions.
/// </summary>
public sealed class MeetingService : INotifyPropertyChanged
{
    public static MeetingService Instance { get; } = new();

    private MeetingState _state = MeetingState.Inactive;
    private string? _currentMeetingId;
    private string? _currentSessionId;
    private DateTime? _startTime;
    private Timer? _durationTimer;

    // Inactivity detection (matches macOS: 5-min threshold, 30s check interval)
    private DateTime _lastTranscriptionTime = DateTime.UtcNow;
    private Timer? _inactivityTimer;
    private const int InactivityThresholdSeconds = 300; // 5 minutes
    private const int InactivityCheckIntervalSeconds = 30;
    private bool _inactivityAlertShown;

    // Summary generation state
    private bool _isSummaryGenerationInProgress;
    public bool IsSummaryGenerationInProgress => _isSummaryGenerationInProgress;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? MeetingListNeedsRefresh;
    public event EventHandler? InactivityAlertTriggered;
    public event EventHandler<MeetingAnalyticsData>? SummaryGenerationComplete;
    public event EventHandler? SummaryGenerationSkipped;

    public MeetingState State
    {
        get => _state;
        private set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsActive)); }
    }

    public bool IsActive => _state == MeetingState.Active || _state == MeetingState.Paused;

    public ObservableCollection<MeetingTranscription> LiveTranscriptions { get; } = new();

    public string? CurrentMeetingId => _currentMeetingId;

    public TimeSpan Duration => _startTime.HasValue ? DateTime.UtcNow - _startTime.Value : TimeSpan.Zero;

    private MeetingService() { }

    public async Task StartMeeting(string? title = null)
    {
        if (IsActive) return;

        State = MeetingState.Starting;
        LiveTranscriptions.Clear();
        FileLogger.Instance.Info("Meeting", "Starting meeting...");

        try
        {
            // Generate session ID (matches macOS BSONObjectID hex format)
            _currentSessionId = Guid.NewGuid().ToString("N").Substring(0, 24);

            // Create meeting via GraphQL
            var meetingTitle = title ?? $"Meeting at {DateTime.Now:MMM dd, yyyy h:mm tt}";
            _currentMeetingId = await CreateMeetingGraphQL(meetingTitle);

            if (string.IsNullOrEmpty(_currentMeetingId))
            {
                FileLogger.Instance.Error("Meeting", "Failed to create meeting via GraphQL");
                State = MeetingState.Error;
                return;
            }

            FileLogger.Instance.Info("Meeting", $"Meeting created: {_currentMeetingId}");

            // Connect meeting WebSocket
            await WebSocketRegistry.Instance.ConnectMeetingTransport(_currentMeetingId);
            var client = WebSocketRegistry.Instance.MeetingClient;

            if (client == null)
            {
                FileLogger.Instance.Error("Meeting", "Failed to connect meeting WebSocket");
                State = MeetingState.Error;
                return;
            }

            // Subscribe to events
            client.OnTranscription += OnTranscriptionReceived;
            client.OnPartialTranscription += OnPartialTranscriptionReceived;
            client.OnConnectionConfirmed += OnConnectionConfirmed;
            client.OnError += OnMeetingError;

            // Send connect payload
            await client.SendConnectPayload(_currentSessionId, meetingTitle);

            // Start audio capture
            AudioService.Instance.OnAudioDataAvailable += OnAudioData;
            AudioService.Instance.StartCapture();

            _startTime = DateTime.UtcNow;
            State = MeetingState.Active;

            // Start duration timer
            _durationTimer = new Timer(_ => OnPropertyChanged(nameof(Duration)),
                null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

            // Start inactivity detection (matches macOS 5-min threshold)
            _lastTranscriptionTime = DateTime.UtcNow;
            _inactivityAlertShown = false;
            _inactivityTimer = new Timer(CheckInactivity, null,
                TimeSpan.FromSeconds(InactivityCheckIntervalSeconds),
                TimeSpan.FromSeconds(InactivityCheckIntervalSeconds));

            // Update ViewCoordinator
            ViewCoordinator.Instance.MeetingState = MeetingState.Active;

            FileLogger.Instance.Info("Meeting", "Meeting started successfully");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Meeting", $"Start failed: {ex.Message}");
            State = MeetingState.Error;
        }
    }

    public async Task StopMeeting()
    {
        if (!IsActive) return;

        FileLogger.Instance.Info("Meeting", "Stopping meeting...");

        // Stop audio capture
        AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
        AudioService.Instance.StopCapture();
        _durationTimer?.Dispose();
        _durationTimer = null;
        _inactivityTimer?.Dispose();
        _inactivityTimer = null;

        try
        {
            // Unsubscribe event handlers to prevent leaks
            var client = WebSocketRegistry.Instance.MeetingClient;
            if (client != null)
            {
                client.OnTranscription -= OnTranscriptionReceived;
                client.OnPartialTranscription -= OnPartialTranscriptionReceived;
                client.OnConnectionConfirmed -= OnConnectionConfirmed;
                client.OnError -= OnMeetingError;
                await client.SendFinalSignal();
            }

            // Wait briefly for final transcriptions
            await Task.Delay(1000);

            // Disconnect meeting WebSocket
            await WebSocketRegistry.Instance.DisconnectMeetingTransport();
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Meeting", $"Stop error: {ex.Message}");
        }

        ViewCoordinator.Instance.MeetingState = MeetingState.Inactive;
        State = MeetingState.Inactive;
        MeetingListNeedsRefresh?.Invoke(this, EventArgs.Empty);

        FileLogger.Instance.Info("Meeting", "Meeting stopped");
    }

    public void PauseMeeting()
    {
        if (State == MeetingState.Active)
        {
            State = MeetingState.Paused;
            AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
            AudioService.Instance.StopCapture();
        }
    }

    public void ResumeMeeting()
    {
        if (State == MeetingState.Paused)
        {
            State = MeetingState.Active;
            AudioService.Instance.OnAudioDataAvailable += OnAudioData;
            AudioService.Instance.StartCapture();
        }
    }

    public void DismissResult()
    {
        State = MeetingState.Inactive;
        _currentMeetingId = null;
        _currentSessionId = null;
        _startTime = null;
    }

    private async Task<string?> CreateMeetingGraphQL(string title)
    {
        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("meeting_api");
            if (baseUrl == null) return null;

            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (string.IsNullOrEmpty(workspaceId))
            {
                FileLogger.Instance.Error("Meeting", "WorkspaceId not available for GraphQL");
                return null;
            }

            // GraphQL mutation to create meeting
            // URL format: {baseUrl}/{workspaceId}/graphql (matches macOS)
            var graphqlPayload = new
            {
                query = @"mutation Mutation($input: MeetingInput) {
                    startMeeting(input: $input) {
                        _id
                    }
                }",
                variables = new
                {
                    input = new
                    {
                        isAiIntelligenceEnabled = false,
                        title,
                        transcriptionSource = "in_app_meeting"
                    }
                }
            };

            var response = await NetworkService.Instance.PostRawAsync(
                $"{baseUrl}/{workspaceId}/graphql", graphqlPayload);

            if (response == null) return null;

            var json = JObject.Parse(response);
            var meetingId = json["data"]?["startMeeting"]?["_id"]?.ToString();
            return meetingId;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Meeting", $"GraphQL create failed: {ex.Message}");
            return null;
        }
    }

    private void OnAudioData(object? sender, byte[] data)
    {
        var client = WebSocketRegistry.Instance.MeetingClient;
        _ = client?.SendAudioData(data);
    }

    private static readonly int MaxTranscriptions = Infrastructure.AppConfiguration.Instance.MaxLiveTranscriptions;

    private void OnTranscriptionReceived(object? sender, MeetingTranscription transcription)
    {
        _lastTranscriptionTime = DateTime.UtcNow;
        _inactivityAlertShown = false; // Reset alert if we get new transcription

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            // Cap transcriptions to prevent unbounded memory growth
            if (LiveTranscriptions.Count >= MaxTranscriptions)
            {
                LiveTranscriptions.RemoveAt(0);
            }
            LiveTranscriptions.Add(transcription);
            FileLogger.Instance.Info("Meeting",
                $"Transcription [{transcription.Speaker}]: {transcription.Text.Substring(0, Math.Min(50, transcription.Text.Length))}");
        });
    }

    private void OnPartialTranscriptionReceived(object? sender, MeetingTranscription transcription)
    {
        // Update last partial transcription or add new one
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            // Find existing partial from same speaker
            var existing = LiveTranscriptions.LastOrDefault(t =>
                t.Speaker == transcription.Speaker && !t.IsFinal);
            if (existing != null)
            {
                var idx = LiveTranscriptions.IndexOf(existing);
                LiveTranscriptions[idx] = transcription;
            }
            else
            {
                LiveTranscriptions.Add(transcription);
            }
        });
    }

    private void OnConnectionConfirmed(object? sender, EventArgs e)
    {
        FileLogger.Instance.Info("Meeting", "Meeting WebSocket connection confirmed");
    }

    private void OnMeetingError(object? sender, string error)
    {
        FileLogger.Instance.Error("Meeting", $"Meeting error: {error}");
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            ViewCoordinator.Instance.ErrorMessage = error;
        });
    }

    // --- Inactivity Detection (matches macOS 5-min threshold) ---

    private void CheckInactivity(object? state)
    {
        if (!IsActive || _inactivityAlertShown) return;

        var elapsed = (DateTime.UtcNow - _lastTranscriptionTime).TotalSeconds;
        if (elapsed >= InactivityThresholdSeconds)
        {
            _inactivityAlertShown = true;
            FileLogger.Instance.Warning("Meeting", $"No transcription for {elapsed:F0}s — triggering inactivity alert");
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                InactivityAlertTriggered?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    /// <summary>User confirmed still in meeting — reset inactivity timer.</summary>
    public void ContinueFromInactivityAlert()
    {
        _lastTranscriptionTime = DateTime.UtcNow;
        _inactivityAlertShown = false;
        FileLogger.Instance.Info("Meeting", "User confirmed still in meeting");
    }

    /// <summary>User chose to end meeting from inactivity alert.</summary>
    public async Task EndMeetingFromInactivityAlert()
    {
        FileLogger.Instance.Info("Meeting", "User ended meeting from inactivity alert");
        await StopMeeting();
    }

    // --- Summary Generation via WebSocket ---

    /// <summary>
    /// Trigger streaming summary generation via Summary WebSocket.
    /// Matches macOS: connects to genarateSummary/ws, sends trigger, receives streaming analytics.
    /// </summary>
    public async Task<bool> TriggerSummaryGeneration(string meetingId)
    {
        if (_isSummaryGenerationInProgress) return false;
        if (IsActive) return false; // Cannot generate while meeting is active

        _isSummaryGenerationInProgress = true;
        OnPropertyChanged(nameof(IsSummaryGenerationInProgress));

        try
        {
            await WebSocketRegistry.Instance.ConnectSummaryTransport(meetingId);
            var client = WebSocketRegistry.Instance.SummaryClient;
            if (client == null)
            {
                FileLogger.Instance.Error("Meeting", "Failed to connect summary WebSocket");
                _isSummaryGenerationInProgress = false;
                return false;
            }

            // Use named handlers so they can self-unsubscribe (prevents leaks on repeated calls)
            void OnComplete(object? s, MeetingAnalyticsData analytics)
            {
                client.OnSummaryComplete -= OnComplete;
                client.OnSummarySkipped -= OnSkipped;
                client.OnError -= OnSummaryError;
                _isSummaryGenerationInProgress = false;
                OnPropertyChanged(nameof(IsSummaryGenerationInProgress));
                WebSocketRegistry.Instance.DisconnectSummaryTransport();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    SummaryGenerationComplete?.Invoke(this, analytics);
                });
            }

            void OnSkipped(object? s, EventArgs e)
            {
                client.OnSummaryComplete -= OnComplete;
                client.OnSummarySkipped -= OnSkipped;
                client.OnError -= OnSummaryError;
                _isSummaryGenerationInProgress = false;
                OnPropertyChanged(nameof(IsSummaryGenerationInProgress));
                WebSocketRegistry.Instance.DisconnectSummaryTransport();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    SummaryGenerationSkipped?.Invoke(this, EventArgs.Empty);
                });
            }

            void OnSummaryError(object? s, string error)
            {
                client.OnSummaryComplete -= OnComplete;
                client.OnSummarySkipped -= OnSkipped;
                client.OnError -= OnSummaryError;
                _isSummaryGenerationInProgress = false;
                OnPropertyChanged(nameof(IsSummaryGenerationInProgress));
                WebSocketRegistry.Instance.DisconnectSummaryTransport();
            }

            client.OnSummaryComplete += OnComplete;
            client.OnSummarySkipped += OnSkipped;
            client.OnError += OnSummaryError;

            // Generate session ID and get tenant ID
            var sessionId = Guid.NewGuid().ToString("N").Substring(0, 24);
            var tenantId = AuthManager.Instance.Storage.TenantId ?? "";

            await client.SendTriggerPayload(sessionId, tenantId);
            FileLogger.Instance.Info("Meeting", $"Summary generation triggered for {meetingId}");
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Meeting", $"Summary generation failed: {ex.Message}");
            _isSummaryGenerationInProgress = false;
            OnPropertyChanged(nameof(IsSummaryGenerationInProgress));
            return false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
