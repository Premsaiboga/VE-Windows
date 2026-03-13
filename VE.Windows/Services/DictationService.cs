using System.ComponentModel;
using System.Runtime.CompilerServices;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.WebSocket;

namespace VE.Windows.Services;

public enum DictationState
{
    Inactive,
    Waiting,
    Recording,
    Processing,
    Success,
    Error
}

/// <summary>
/// Voice dictation workflow coordinator.
/// Equivalent to macOS DictationService.
/// </summary>
public sealed class DictationService : INotifyPropertyChanged
{
    public static DictationService Instance { get; } = new();

    private DictationState _state = DictationState.Inactive;
    private string _transcribedText = "";
    private string? _errorMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DictationState State
    {
        get => _state;
        private set { _state = value; OnPropertyChanged(); }
    }

    public string TranscribedText
    {
        get => _transcribedText;
        private set { _transcribedText = value; OnPropertyChanged(); }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { _errorMessage = value; OnPropertyChanged(); }
    }

    private DictationService() { }

    public async Task StartDictation()
    {
        if (State != DictationState.Inactive) return;

        FileLogger.Instance.Info("Dictation", "Starting dictation...");
        State = DictationState.Waiting;
        TranscribedText = "";
        ErrorMessage = null;

        try
        {
            // Connect WebSocket if needed
            var client = WebSocketRegistry.Instance.VoiceToTextClient;
            if (client == null || !client.IsConnected)
            {
                await WebSocketRegistry.Instance.ConnectDictationTransport();
                client = WebSocketRegistry.Instance.VoiceToTextClient;
            }

            if (client == null)
            {
                State = DictationState.Error;
                ErrorMessage = "Failed to connect to dictation service";
                return;
            }

            // Subscribe to transcription events
            client.OnTranscriptionReceived += OnTranscriptionReceived;
            client.OnTranscriptionComplete += OnTranscriptionComplete;
            client.OnError += OnDictationError;

            // Start audio capture
            AudioService.Instance.OnAudioDataAvailable += OnAudioData;
            AudioService.Instance.StartCapture();

            State = DictationState.Recording;
            FileLogger.Instance.Info("Dictation", "Recording started");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Dictation", $"Start failed: {ex.Message}");
            State = DictationState.Error;
            ErrorMessage = ex.Message;
        }
    }

    public async Task StopDictation()
    {
        if (State != DictationState.Recording) return;

        FileLogger.Instance.Info("Dictation", "Stopping dictation...");
        AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
        AudioService.Instance.StopCapture();

        State = DictationState.Processing;

        // Send end signal to WebSocket
        var client = WebSocketRegistry.Instance.VoiceToTextClient;
        if (client != null)
        {
            await client.SendEndSignal();
        }

        // Auto-timeout if processing takes too long
        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ =>
        {
            if (State == DictationState.Processing)
            {
                State = DictationState.Error;
                ErrorMessage = "Processing timed out";
            }
        });
    }

    public void CancelDictation()
    {
        AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
        AudioService.Instance.StopCapture();
        CleanupSubscriptions();
        State = DictationState.Inactive;
        TranscribedText = "";
        ErrorMessage = null;
    }

    private void OnAudioData(object? sender, byte[] data)
    {
        var client = WebSocketRegistry.Instance.VoiceToTextClient;
        _ = client?.SendAudioData(data);
    }

    private void OnTranscriptionReceived(object? sender, string text)
    {
        TranscribedText = text;
    }

    private void OnTranscriptionComplete(object? sender, string finalText)
    {
        TranscribedText = finalText;
        CleanupSubscriptions();

        if (!string.IsNullOrWhiteSpace(finalText))
        {
            State = DictationState.Success;
            // Auto-paste transcribed text
            ClipboardManager.Instance.PasteText(finalText);
            FileLogger.Instance.Info("Dictation", $"Success: {finalText.Length} chars pasted");

            // Auto-dismiss after 3 seconds
            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                if (State == DictationState.Success) State = DictationState.Inactive;
            });
        }
        else
        {
            State = DictationState.Error;
            ErrorMessage = "No transcription received";
        }
    }

    private void OnDictationError(object? sender, string error)
    {
        CleanupSubscriptions();
        AudioService.Instance.StopCapture();
        State = DictationState.Error;
        ErrorMessage = error;
    }

    private void CleanupSubscriptions()
    {
        var client = WebSocketRegistry.Instance.VoiceToTextClient;
        if (client != null)
        {
            client.OnTranscriptionReceived -= OnTranscriptionReceived;
            client.OnTranscriptionComplete -= OnTranscriptionComplete;
            client.OnError -= OnDictationError;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
