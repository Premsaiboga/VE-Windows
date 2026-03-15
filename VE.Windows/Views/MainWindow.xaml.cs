using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.ViewModels;
using VE.Windows.Views.FloatingWindow;
using VE.Windows.Views.Notch;

namespace VE.Windows.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private FloatingPanelWindow? _floatingPanel;
    private Timer? _topmostTimer;

    // Win32 imports for always-on-top
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // Subscribe to keyboard events
        KeyboardHookManager.Instance.OnEscapePressed += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_vm.IsOpen) AnimateClose();
                if (_floatingPanel?.IsVisible == true) _floatingPanel.Hide();

                // Cancel prediction on ESC
                if (ViewCoordinator.Instance.CombinedPredictionState != CombinedPredictionState.Inactive)
                {
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Inactive;
                    ClosedContent.ResetToIdle();
                    var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
                    _ = client?.SendStopAction();
                }

                // Cancel dictation on ESC
                if (Services.DictationService.Instance.State != Services.DictationState.Inactive)
                {
                    Services.DictationService.Instance.CancelDictation();
                    ClosedContent.ResetToIdle();
                }
            });
        };

        KeyboardHookManager.Instance.OnPredictionTriggered += (s, e) =>
        {
            Dispatcher.Invoke(() => OnPredictionTriggered());
        };

        KeyboardHookManager.Instance.OnPredictionReleased += (s, e) =>
        {
            Dispatcher.Invoke(() => OnPredictionReleased());
        };

        KeyboardHookManager.Instance.OnPredictionTapped += (s, e) =>
        {
            Dispatcher.Invoke(() => OnPredictionTapped());
        };

        KeyboardHookManager.Instance.OnDictationTriggered += (s, e) =>
        {
            Dispatcher.Invoke(() => OnDictationTriggered());
        };

        KeyboardHookManager.Instance.OnDictationReleased += (s, e) =>
        {
            Dispatcher.Invoke(() => OnDictationReleased());
        };

        KeyboardHookManager.Instance.OnInstructionTriggered += (s, e) =>
        {
            Dispatcher.Invoke(() => ShowFloatingPanel());
        };

        KeyboardHookManager.Instance.OnMeetingToggled += (s, e) =>
        {
            Dispatcher.Invoke(() => OnMeetingToggled());
        };

        // Auth state changes
        AuthManager.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AuthManager.AuthState))
            {
                Dispatcher.Invoke(() =>
                {
                    if (AuthManager.Instance.IsAuthenticated)
                    {
                        if (_vm.IsOpen) AnimateClose();
                        ClosedContent.ShowWelcome();
                    }
                });
            }
        };

        // ViewCoordinator state changes
        ViewCoordinator.Instance.PropertyChanged += (s, e) =>
        {
            // No-op: notch background stays black
        };

        // Wire dictation errors to ERROR CAPSULE (below notch, not in notch)
        Services.DictationService.Instance.OnDictationError += (s, error) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                ClosedContent.ResetToIdle();
                ErrorCapsuleWindow.ShowError(error ?? "Dictation error");
            });
        };

        Services.DictationService.Instance.OnDictationProcessing += (s, e) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                ClosedContent.ShowPredictionWaiting(); // Show "processing" dots
            });
        };

        // Dictation success — show "Pasted" in notch (NOT prediction animation)
        Services.DictationService.Instance.OnDictationSuccess += (s, text) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                ClosedContent.ShowDictationSuccess();

                // Auto-reset to idle after 3 seconds
                Task.Delay(3000).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (ViewCoordinator.Instance.DictationState == Services.DictationState.Success ||
                            ViewCoordinator.Instance.DictationState == Services.DictationState.Inactive)
                        {
                            ClosedContent.ResetToIdle();
                        }
                    });
                });
            });
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle |= WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        }

        EnsureTopmost();

        _topmostTimer = new Timer(_ =>
        {
            Dispatcher.Invoke(EnsureTopmost);
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        // Monitor display changes (connect/disconnect) — reposition notch
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            FileLogger.Instance.Info("MainWindow", "Display settings changed, repositioning notch");
            PositionWindow();
        });
    }

    private void EnsureTopmost()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }
        catch { }
    }

    private void PositionWindow()
    {
        var pos = _vm.GetWindowPosition();
        Left = pos.X;
        Top = 0;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (!AuthManager.Instance.IsAuthenticated)
            {
                AnimateOpen();
            }
            else
            {
                ShowFloatingPanel();
            }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm.IsOpen)
        {
            AnimateClose();
        }
    }

    private void AnimateOpen()
    {
        if (_vm.IsOpen) return;
        _vm.Open();

        ClosedContent.Visibility = Visibility.Collapsed;
        OpenContent.Visibility = Visibility.Visible;

        var storyboard = (Storyboard)FindResource("OpenAnimation");
        ((DoubleAnimation)storyboard.Children[0]).To = _vm.OpenWidth;
        ((DoubleAnimation)storyboard.Children[1]).To = _vm.OpenHeight;
        storyboard.Begin();

        PositionWindow();
    }

    private void AnimateClose()
    {
        if (!_vm.IsOpen) return;
        _vm.Close();

        var storyboard = (Storyboard)FindResource("CloseAnimation");
        ((DoubleAnimation)storyboard.Children[0]).To = _vm.ClosedWidth;
        ((DoubleAnimation)storyboard.Children[1]).To = _vm.ClosedHeight;
        storyboard.Completed += (s, e) =>
        {
            OpenContent.Visibility = Visibility.Collapsed;
            ClosedContent.Visibility = Visibility.Visible;
        };
        storyboard.Begin();

        PositionWindow();
    }

    // Prediction flow
    private byte[]? _predictionScreenshot;
    private string? _predictionAppName;
    private string? _predictionWindowTitle;
    private bool _predictionAudioSending;
    private readonly List<byte[]> _predictionAudioBuffer = new();
    private bool _predictionAudioBuffering;
    private bool _predictionWsReady;
    private IntPtr _targetWindowHandle; // Capture BEFORE prediction starts

    private void OnPredictionTriggered()
    {
        if (!AuthManager.Instance.IsAuthenticated) return;

        // Capture the target window BEFORE we do anything
        _targetWindowHandle = GetForegroundWindow();

        ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Waiting;
        ClosedContent.ShowPredictionWaiting();
        _predictionAudioSending = false;
        _predictionWsReady = false;

        _predictionAppName = ScreenCaptureManager.Instance.GetActiveAppName();
        _predictionWindowTitle = ScreenCaptureManager.Instance.GetActiveWindowTitle();

        // START AUDIO IMMEDIATELY (before WebSocket connects)
        _predictionAudioBuffer.Clear();
        _predictionAudioBuffering = true;
        _predictionAudioSending = true;
        AudioService.Instance.OnAudioDataAvailable += OnPredictionAudioData;
        AudioService.Instance.StartCapture();

        Task.Run(async () =>
        {
            _predictionScreenshot = ScreenCaptureManager.Instance.CaptureActiveWindow();

            await WebSocket.WebSocketRegistry.Instance.ConnectUnifiedAudioTransport();
            var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;

            var maxWait = DateTime.UtcNow.AddSeconds(5);
            while (client != null && !client.IsConnected && DateTime.UtcNow < maxWait)
            {
                await Task.Delay(100);
            }

            if (client == null || !client.IsConnected)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    AudioService.Instance.OnAudioDataAvailable -= OnPredictionAudioData;
                    AudioService.Instance.StopCapture();
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Error;
                    ClosedContent.ResetToIdle();
                    ErrorCapsuleWindow.ShowError("Connection failed");
                });
                return;
            }

            client.ResetAccumulatedText();

            client.OnPredictionStreaming -= OnPredictionStreamingHandler;
            client.OnPredictionComplete -= OnPredictionCompleteHandler;
            client.OnError -= OnPredictionErrorHandler;

            client.OnPredictionStreaming += OnPredictionStreamingHandler;
            client.OnPredictionComplete += OnPredictionCompleteHandler;
            client.OnError += OnPredictionErrorHandler;

            // Flush buffered audio
            _predictionWsReady = true;
            _predictionAudioBuffering = false;

            lock (_predictionAudioBuffer)
            {
                FileLogger.Instance.Info("Prediction", $"Flushing {_predictionAudioBuffer.Count} buffered audio chunks");
                foreach (var chunk in _predictionAudioBuffer)
                {
                    _ = client.SendAudioChunk(chunk);
                }
                _predictionAudioBuffer.Clear();
            }
        });
    }

    private void OnPredictionStreamingHandler(object? sender, string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Streaming;
            ViewCoordinator.Instance.PredictionText = text;
            ClosedContent.ShowPredictionStreaming(text);
        });
    }

    private void OnPredictionCompleteHandler(object? sender, WebSocket.PredictionResult result)
    {
        // Unsubscribe IMMEDIATELY so dictation doesn't trigger prediction handlers
        UnsubscribePredictionHandlers();

        Dispatcher.BeginInvoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Success;
            Services.PredictionFeedbackService.Instance.OnPredictionSuccess(result.Id);

            // Paste FIRST — before any UI update that could steal focus
            PasteToTargetWindow(result.Text);

            // Delay notch UI update to let paste complete (paste runs on background thread
            // with ~350ms of delays before sending Ctrl+V)
            Task.Delay(600).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ClosedContent.ShowPredictionSuccess(result.Text);

                    Task.Delay(4000).ContinueWith(_ =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Inactive;
                            ClosedContent.ResetToIdle();
                        });
                    });
                });
            });
        });
    }

    private void OnPredictionErrorHandler(object? sender, string error)
    {
        // Unsubscribe IMMEDIATELY so dictation doesn't trigger prediction handlers
        UnsubscribePredictionHandlers();

        Dispatcher.BeginInvoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Error;
            ClosedContent.ResetToIdle();
            ErrorCapsuleWindow.ShowError(error); // Show in capsule below notch
        });
    }

    /// <summary>
    /// Unsubscribe all prediction event handlers from the UnifiedAudioClient.
    /// MUST be called after prediction completes or errors to prevent
    /// dictation/other operations from triggering prediction UI.
    /// </summary>
    private void UnsubscribePredictionHandlers()
    {
        var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
        if (client == null) return;
        client.OnPredictionStreaming -= OnPredictionStreamingHandler;
        client.OnPredictionComplete -= OnPredictionCompleteHandler;
        client.OnPredictionComplete -= OnTapPredictionCompleteHandler;
        client.OnError -= OnPredictionErrorHandler;
    }

    private void OnPredictionAudioData(object? sender, byte[] data)
    {
        if (!_predictionAudioSending) return;

        if (_predictionAudioBuffering)
        {
            lock (_predictionAudioBuffer)
            {
                _predictionAudioBuffer.Add(data);
            }
            return;
        }

        if (_predictionWsReady)
        {
            var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
            _ = client?.SendAudioChunk(data);
        }
    }

    /// <summary>
    /// Quick tap prediction: screenshot-only, no audio.
    /// </summary>
    private void OnPredictionTapped()
    {
        if (!AuthManager.Instance.IsAuthenticated) return;

        // Capture the target window BEFORE we do anything
        _targetWindowHandle = GetForegroundWindow();

        ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Waiting;
        ClosedContent.ShowPredictionWaiting();

        var appName = ScreenCaptureManager.Instance.GetActiveAppName();
        var windowTitle = ScreenCaptureManager.Instance.GetActiveWindowTitle();

        Task.Run(async () =>
        {
            var screenshot = ScreenCaptureManager.Instance.CaptureActiveWindow();

            await WebSocket.WebSocketRegistry.Instance.ConnectUnifiedAudioTransport();
            var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;

            var maxWait = DateTime.UtcNow.AddSeconds(5);
            while (client != null && !client.IsConnected && DateTime.UtcNow < maxWait)
            {
                await Task.Delay(100);
            }

            if (client == null || !client.IsConnected)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Error;
                    ClosedContent.ResetToIdle();
                    ErrorCapsuleWindow.ShowError("Connection failed");
                });
                return;
            }

            client.ResetAccumulatedText();

            client.OnPredictionComplete -= OnTapPredictionCompleteHandler;
            client.OnError -= OnPredictionErrorHandler;

            client.OnPredictionComplete += OnTapPredictionCompleteHandler;
            client.OnError += OnPredictionErrorHandler;

            FileLogger.Instance.Info("Prediction", "Tap prediction: sending screenshot-only payload");
            await client.SendEndPayload(
                audioCompleted: false,
                screenshot: screenshot,
                appName: appName,
                windowTitle: windowTitle);
        });
    }

    private void OnTapPredictionCompleteHandler(object? sender, WebSocket.PredictionResult result)
    {
        // Unsubscribe IMMEDIATELY
        UnsubscribePredictionHandlers();

        Dispatcher.BeginInvoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Success;
            Services.PredictionFeedbackService.Instance.OnPredictionSuccess(result.Id);

            // Paste FIRST — before any UI update that could steal focus
            PasteToTargetWindow(result.Text);

            // Delay notch UI update to let paste complete
            Task.Delay(600).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ClosedContent.ShowPredictionSuccess(result.Text);

                    Task.Delay(4000).ContinueWith(_ =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Inactive;
                            ClosedContent.ResetToIdle();
                        });
                    });
                });
            });
        });
    }

    /// <summary>
    /// Paste text to the target window that was active when prediction started.
    /// </summary>
    private void PasteToTargetWindow(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // Store the target handle for ClipboardManager to use
        ClipboardManager.Instance.PasteTextToWindow(text, _targetWindowHandle);
    }

    private void OnPredictionReleased()
    {
        _predictionAudioBuffering = false;
        AudioService.Instance.OnAudioDataAvailable -= OnPredictionAudioData;
        AudioService.Instance.StopCapture();

        Task.Run(async () =>
        {
            var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
            if (client != null)
            {
                lock (_predictionAudioBuffer)
                {
                    foreach (var chunk in _predictionAudioBuffer)
                    {
                        _ = client.SendAudioChunk(chunk);
                    }
                    _predictionAudioBuffer.Clear();
                }

                await client.SendEndPayload(
                    audioCompleted: _predictionAudioSending,
                    screenshot: _predictionScreenshot,
                    appName: _predictionAppName,
                    windowTitle: _predictionWindowTitle);
            }
        });
    }

    // Meeting flow
    private void OnMeetingToggled()
    {
        if (!AuthManager.Instance.IsAuthenticated) return;

        if (Services.MeetingService.Instance.IsActive)
        {
            _ = Services.MeetingService.Instance.StopMeeting();
        }
        else
        {
            _ = Services.MeetingService.Instance.StartMeeting();
        }
    }

    // Dictation flow (hold key to record, release to process)
    private void OnDictationTriggered()
    {
        // Capture target window for dictation paste
        _targetWindowHandle = GetForegroundWindow();
        Services.DictationService.Instance.TargetWindowHandle = _targetWindowHandle;
        ViewCoordinator.Instance.DictationState = Services.DictationState.Waiting;
        ClosedContent.ShowDictationWaiting();
        _ = Services.DictationService.Instance.StartDictation();
    }

    private void OnDictationReleased()
    {
        _ = Services.DictationService.Instance.StopDictation();
    }

    private void ShowFloatingPanel()
    {
        if (!AuthManager.Instance.IsAuthenticated) return;

        if (_floatingPanel == null)
        {
            _floatingPanel = new FloatingPanelWindow();
        }

        if (_floatingPanel.IsVisible)
        {
            _floatingPanel.Hide();
        }
        else
        {
            _floatingPanel.ShowAndActivate();
        }
    }
}
