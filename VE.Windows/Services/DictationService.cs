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
/// Matches macOS DictationService: uses unified audio endpoint (same as prediction)
/// with direct_agent="dictation". Sends audio chunks + end payload, receives enhanced_text.
/// </summary>
public sealed class DictationService : INotifyPropertyChanged
{
    public static DictationService Instance { get; } = new();

    private DictationState _state = DictationState.Inactive;
    private string _transcribedText = "";
    private string? _errorMessage;
    private string? _dictationAppName;
    private string? _dictationWindowTitle;
    private DateTime _dictationStartTime;
    private bool _stopRequested;
    private UnifiedAudioSocketClient? _activeClient;

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
        _stopRequested = false;
        _dictationStartTime = DateTime.UtcNow;

        // Capture context before VE window activates
        _dictationAppName = ScreenCaptureManager.Instance.GetActiveAppName();
        _dictationWindowTitle = ScreenCaptureManager.Instance.GetActiveWindowTitle();

        try
        {
            // Use unified audio endpoint (same as prediction) - matches macOS behavior
            await WebSocketRegistry.Instance.ConnectUnifiedAudioTransport();
            var client = WebSocketRegistry.Instance.UnifiedAudioClient;

            // Wait for connection
            var maxWait = DateTime.UtcNow.AddSeconds(5);
            while (client != null && !client.IsConnected && DateTime.UtcNow < maxWait)
            {
                await Task.Delay(100);
            }

            if (client == null || !client.IsConnected)
            {
                FileLogger.Instance.Error("Dictation", "Failed to connect WebSocket");
                State = DictationState.Error;
                ErrorMessage = "Failed to connect to dictation service";
                ViewCoordinator.Instance.DictationState = DictationState.Inactive;
                return;
            }

            _activeClient = client;
            client.ResetAccumulatedText();

            // Subscribe to dictation result event (enhanced_text response)
            client.OnDictationResult -= OnDictationResult;
            client.OnError -= OnDictationError;
            client.OnDictationResult += OnDictationResult;
            client.OnError += OnDictationError;

            // If stop was requested while we were connecting, handle it now
            if (_stopRequested)
            {
                FileLogger.Instance.Info("Dictation", "Stop was requested during connection, stopping immediately");
                await FinishDictation(client);
                return;
            }

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
            ViewCoordinator.Instance.DictationState = DictationState.Inactive;
        }
    }

    public async Task StopDictation()
    {
        if (State == DictationState.Inactive || State == DictationState.Processing) return;

        FileLogger.Instance.Info("Dictation", $"Stopping dictation (state: {State})...");

        // If still waiting for connection, set flag so StartDictation handles it
        if (State == DictationState.Waiting)
        {
            _stopRequested = true;
            return;
        }

        AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
        AudioService.Instance.StopCapture();

        var client = _activeClient ?? WebSocketRegistry.Instance.UnifiedAudioClient;
        if (client != null)
        {
            await FinishDictation(client);
        }
        else
        {
            State = DictationState.Error;
            ErrorMessage = "No active connection";
            ViewCoordinator.Instance.DictationState = DictationState.Inactive;
        }
    }

    private async Task FinishDictation(UnifiedAudioSocketClient client)
    {
        State = DictationState.Processing;

        try
        {
            await client.SendDictationEndPayload(
                appName: _dictationAppName,
                windowTitle: _dictationWindowTitle);
            FileLogger.Instance.Info("Dictation", "End payload sent, waiting for response...");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Dictation", $"End payload failed: {ex.Message}");
            State = DictationState.Error;
            ErrorMessage = "Failed to send audio for processing";
            ViewCoordinator.Instance.DictationState = DictationState.Inactive;
            return;
        }

        // Auto-timeout if processing takes too long
        _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ =>
        {
            if (State == DictationState.Processing)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    State = DictationState.Error;
                    ErrorMessage = "Processing timed out";
                    ViewCoordinator.Instance.DictationState = DictationState.Inactive;
                });
            }
        });
    }

    public void CancelDictation()
    {
        _stopRequested = true;
        AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
        AudioService.Instance.StopCapture();
        CleanupSubscriptions();
        State = DictationState.Inactive;
        TranscribedText = "";
        ErrorMessage = null;
        ViewCoordinator.Instance.DictationState = DictationState.Inactive;
    }

    private void OnAudioData(object? sender, byte[] data)
    {
        var client = _activeClient ?? WebSocketRegistry.Instance.UnifiedAudioClient;
        if (client != null)
        {
            _ = client.SendAudioChunk(data);
        }
    }

    private void OnDictationResult(object? sender, string enhancedText)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            TranscribedText = enhancedText;
            CleanupSubscriptions();

            if (!string.IsNullOrWhiteSpace(enhancedText))
            {
                State = DictationState.Success;
                ClipboardManager.Instance.PasteText(enhancedText);
                FileLogger.Instance.Info("Dictation", $"Success: {enhancedText.Length} chars pasted");
                ViewCoordinator.Instance.DictationState = DictationState.Inactive;

                _ = Task.Delay(3000).ContinueWith(_ =>
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        if (State == DictationState.Success) State = DictationState.Inactive;
                    });
                });
            }
            else
            {
                State = DictationState.Error;
                ErrorMessage = "No transcription received";
                ViewCoordinator.Instance.DictationState = DictationState.Inactive;
            }
        });
    }

    private void OnDictationError(object? sender, string error)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            FileLogger.Instance.Error("Dictation", $"Error received: {error}");
            CleanupSubscriptions();
            AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
            AudioService.Instance.StopCapture();
            State = DictationState.Error;
            ErrorMessage = error;
            ViewCoordinator.Instance.DictationState = DictationState.Inactive;
        });
    }

    private void CleanupSubscriptions()
    {
        var client = _activeClient ?? WebSocketRegistry.Instance.UnifiedAudioClient;
        if (client != null)
        {
            client.OnDictationResult -= OnDictationResult;
            client.OnError -= OnDictationError;
        }
        _activeClient = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
