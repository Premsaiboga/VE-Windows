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
/// Pre-starts audio capture immediately (like macOS preStartAudioCapture) to avoid losing audio.
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
    private volatile bool _stopRequested;
    private UnifiedAudioSocketClient? _activeClient;
    private CancellationTokenSource? _timeoutCts;

    // Audio buffering - capture audio immediately, buffer until WebSocket connects
    private readonly List<byte[]> _audioBuffer = new();
    private volatile bool _isBuffering;
    private volatile bool _webSocketReady;
    private static readonly int MaxAudioBufferChunks = Infrastructure.AppConfiguration.Instance.AudioBufferMaxChunks;

    /// <summary>
    /// Target window handle to paste dictation result into.
    /// Set by MainWindow before starting dictation.
    /// </summary>
    public IntPtr TargetWindowHandle { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Events for notifying UI (MainWindow listens to these)
    public event EventHandler<string>? OnDictationError;
    public event EventHandler? OnDictationStarted;
    public event EventHandler? OnDictationProcessing;
    public event EventHandler<string>? OnDictationSuccess;

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
        _webSocketReady = false;
        _dictationStartTime = DateTime.UtcNow;

        // Capture context before VE window activates
        _dictationAppName = ScreenCaptureManager.Instance.GetActiveAppName();
        _dictationWindowTitle = ScreenCaptureManager.Instance.GetActiveWindowTitle();

        // START AUDIO IMMEDIATELY (before WebSocket connects) - matches macOS preStartAudioCapture
        _audioBuffer.Clear();
        _isBuffering = true;
        AudioService.Instance.OnAudioDataAvailable += OnAudioData;
        AudioService.Instance.StartCapture();

        // Check if audio actually started — if no mic, abort immediately
        if (!AudioService.Instance.IsRecording)
        {
            FileLogger.Instance.Error("Dictation", "Audio capture failed (no microphone)");
            AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
            State = DictationState.Error;
            ErrorMessage = "No microphone available";
            OnDictationError?.Invoke(this, ErrorMessage);
            ViewCoordinator.Instance.DictationState = DictationState.Inactive;
            return;
        }

        State = DictationState.Recording;
        OnDictationStarted?.Invoke(this, EventArgs.Empty);
        FileLogger.Instance.Info("Dictation", "Audio capture started immediately (buffering until WebSocket connects)");

        try
        {
            // Connect WebSocket while audio is already being captured
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
                AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
                AudioService.Instance.StopCapture();
                State = DictationState.Error;
                ErrorMessage = "Failed to connect to dictation service";
                OnDictationError?.Invoke(this, ErrorMessage);
                ViewCoordinator.Instance.DictationState = DictationState.Inactive;
                return;
            }

            _activeClient = client;
            client.ResetAccumulatedText();

            // Subscribe to dictation result event (enhanced_text response)
            client.OnDictationResult -= OnDictationResultHandler;
            client.OnError -= OnDictationErrorHandler;
            client.OnDictationResult += OnDictationResultHandler;
            client.OnError += OnDictationErrorHandler;

            // Flush buffered audio to WebSocket (like macOS flushBufferedAudioOnConnect)
            _webSocketReady = true;
            _isBuffering = false;

            lock (_audioBuffer)
            {
                FileLogger.Instance.Info("Dictation", $"Flushing {_audioBuffer.Count} buffered audio chunks to WebSocket");
                foreach (var chunk in _audioBuffer)
                {
                    _ = client.SendAudioChunk(chunk);
                }
                _audioBuffer.Clear();
            }

            // If stop was requested while we were connecting, handle it now
            if (_stopRequested)
            {
                FileLogger.Instance.Info("Dictation", "Stop was requested during connection, stopping immediately");
                AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
                AudioService.Instance.StopCapture();
                await FinishDictation(client);
                return;
            }

            FileLogger.Instance.Info("Dictation", "WebSocket connected, audio streaming live");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Dictation", $"Start failed: {ex.Message}");
            AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
            AudioService.Instance.StopCapture();
            State = DictationState.Error;
            ErrorMessage = ex.Message;
            OnDictationError?.Invoke(this, ErrorMessage);
            ViewCoordinator.Instance.DictationState = DictationState.Inactive;
        }
    }

    public async Task StopDictation()
    {
        if (State == DictationState.Inactive || State == DictationState.Processing) return;

        FileLogger.Instance.Info("Dictation", $"Stopping dictation (state: {State})...");

        // Always stop audio capture
        AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
        AudioService.Instance.StopCapture();

        // If WebSocket not ready yet, set flag so StartDictation handles it after connect
        if (!_webSocketReady)
        {
            FileLogger.Instance.Info("Dictation", "WebSocket not ready, setting stop flag");
            _stopRequested = true;
            return;
        }

        var client = _activeClient ?? WebSocketRegistry.Instance.UnifiedAudioClient;
        if (client != null)
        {
            await FinishDictation(client);
        }
        else
        {
            State = DictationState.Error;
            ErrorMessage = "No active connection";
            OnDictationError?.Invoke(this, ErrorMessage);
            ViewCoordinator.Instance.DictationState = DictationState.Inactive;
        }
    }

    private async Task FinishDictation(UnifiedAudioSocketClient client)
    {
        State = DictationState.Processing;
        OnDictationProcessing?.Invoke(this, EventArgs.Empty);

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
            OnDictationError?.Invoke(this, ErrorMessage);
            ViewCoordinator.Instance.DictationState = DictationState.Inactive;
            return;
        }

        // Auto-timeout if processing takes too long — cancellable via CancellationToken
        _timeoutCts?.Cancel();
        _timeoutCts?.Dispose();
        _timeoutCts = new CancellationTokenSource();
        var token = _timeoutCts.Token;
        _ = Task.Delay(TimeSpan.FromSeconds(15), token).ContinueWith(t =>
        {
            if (!t.IsCanceled && State == DictationState.Processing)
            {
                Helpers.DispatcherHelper.PostOnUI(() =>
                {
                    State = DictationState.Error;
                    ErrorMessage = "Processing timed out";
                    OnDictationError?.Invoke(this, ErrorMessage);
                    ViewCoordinator.Instance.DictationState = DictationState.Inactive;
                });
            }
        }, CancellationToken.None);
    }

    public void CancelDictation()
    {
        _timeoutCts?.Cancel();
        _stopRequested = true;
        _isBuffering = false;
        _webSocketReady = false;
        AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
        AudioService.Instance.StopCapture();
        CleanupSubscriptions();
        lock (_audioBuffer) { _audioBuffer.Clear(); }
        State = DictationState.Inactive;
        TranscribedText = "";
        ErrorMessage = null;
        ViewCoordinator.Instance.DictationState = DictationState.Inactive;
    }

    private void OnAudioData(object? sender, byte[] data)
    {
        if (_isBuffering)
        {
            // Buffer audio until WebSocket is ready (cap to prevent unbounded growth)
            lock (_audioBuffer)
            {
                if (_audioBuffer.Count < MaxAudioBufferChunks)
                    _audioBuffer.Add(data);
            }
            return;
        }

        if (_webSocketReady)
        {
            var client = _activeClient ?? WebSocketRegistry.Instance.UnifiedAudioClient;
            if (client != null)
            {
                _ = client.SendAudioChunk(data);
            }
        }
    }

    private void OnDictationResultHandler(object? sender, string enhancedText)
    {
        _timeoutCts?.Cancel();
        Helpers.DispatcherHelper.PostOnUI(() =>
        {
            TranscribedText = enhancedText;
            CleanupSubscriptions();

            if (!string.IsNullOrWhiteSpace(enhancedText))
            {
                State = DictationState.Success;
                ViewCoordinator.Instance.DictationState = DictationState.Success;

                // Paste to target window
                if (TargetWindowHandle != IntPtr.Zero)
                    ClipboardManager.Instance.PasteTextToWindow(enhancedText, TargetWindowHandle);
                else
                    ClipboardManager.Instance.PasteText(enhancedText);

                FileLogger.Instance.Info("Dictation", $"Success: {enhancedText.Length} chars pasted");

                // Notify UI to show "Pasted" state (NOT prediction animation)
                OnDictationSuccess?.Invoke(this, enhancedText);

                // Auto-reset to idle after 3 seconds
                _ = Task.Delay(3000).ContinueWith(_ =>
                {
                    Helpers.DispatcherHelper.PostOnUI(() =>
                    {
                        if (State == DictationState.Success)
                        {
                            State = DictationState.Inactive;
                            ViewCoordinator.Instance.DictationState = DictationState.Inactive;
                        }
                    });
                });
            }
            else
            {
                State = DictationState.Error;
                ErrorMessage = "No transcription received";
                OnDictationError?.Invoke(this, ErrorMessage);
                ViewCoordinator.Instance.DictationState = DictationState.Inactive;
            }
        });
    }

    private void OnDictationErrorHandler(object? sender, string error)
    {
        _timeoutCts?.Cancel();
        Helpers.DispatcherHelper.PostOnUI(() =>
        {
            FileLogger.Instance.Error("Dictation", $"Error received: {error}");
            CleanupSubscriptions();
            AudioService.Instance.OnAudioDataAvailable -= OnAudioData;
            AudioService.Instance.StopCapture();
            State = DictationState.Error;
            ErrorMessage = error;
            OnDictationError?.Invoke(this, error);
            ViewCoordinator.Instance.DictationState = DictationState.Inactive;
        });
    }

    private void CleanupSubscriptions()
    {
        var client = _activeClient ?? WebSocketRegistry.Instance.UnifiedAudioClient;
        if (client != null)
        {
            client.OnDictationResult -= OnDictationResultHandler;
            client.OnError -= OnDictationErrorHandler;
        }
        _activeClient = null;
        _isBuffering = false;
        _webSocketReady = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
