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

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? MeetingListNeedsRefresh;

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

        try
        {
            // Send final signal
            var client = WebSocketRegistry.Instance.MeetingClient;
            if (client != null)
            {
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

    private void OnTranscriptionReceived(object? sender, MeetingTranscription transcription)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
