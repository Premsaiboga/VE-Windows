using System.Windows;
using System.Windows.Input;
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
    private bool _isHovering;
    private CancellationTokenSource? _hoverCts;
    private FloatingPanelWindow? _floatingPanel;

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

        // NotchHomeView settings button → open floating panel
        NotchHomeView.OnFloatingPanelRequested += (s, e) =>
        {
            Dispatcher.Invoke(() => ShowFloatingPanel());
        };

        // Auth state changes
        AuthManager.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AuthManager.AuthState))
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateNotchBackground();

                    // Show welcome in closed notch after successful login
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
            Dispatcher.Invoke(UpdateNotchBackground);
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        UpdateNotchBackground();
    }

    private void PositionWindow()
    {
        var pos = _vm.GetWindowPosition();
        Left = pos.X;
        Top = 0;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !_vm.IsOpen)
        {
            if (!AuthManager.Instance.IsAuthenticated)
            {
                AnimateOpen();
            }
            else
            {
                // When authenticated, clicking notch toggles floating panel
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

    private void NotchBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        _isHovering = true;
        if (!_vm.IsOpen)
        {
            _hoverCts?.Cancel();
            _hoverCts = new CancellationTokenSource();
            var ct = _hoverCts.Token;

            Task.Delay(300).ContinueWith(_ =>
            {
                if (!ct.IsCancellationRequested && _isHovering && !_vm.IsOpen)
                {
                    Dispatcher.Invoke(AnimateOpen);
                }
            });
        }
    }

    private void NotchBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        _isHovering = false;
        _hoverCts?.Cancel();

        if (_vm.IsOpen)
        {
            Task.Delay(500).ContinueWith(_ =>
            {
                if (!_isHovering)
                {
                    Dispatcher.Invoke(AnimateClose);
                }
            });
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

    private void UpdateNotchBackground()
    {
        var auth = AuthManager.Instance;
        var coord = ViewCoordinator.Instance;

        Color bgColor;

        // Always black notch background
        bgColor = Colors.Black;

        NotchBg.Color = bgColor;
    }

    // Prediction flow - matches macOS:
    // Key down → start audio + screenshot capture → stream audio chunks via WS
    // Key up → stop audio → send end payload with screenshot + metadata → wait for response
    private byte[]? _predictionScreenshot;
    private string? _predictionAppName;
    private string? _predictionWindowTitle;
    private bool _predictionAudioSending;

    private void OnPredictionTriggered()
    {
        if (!AuthManager.Instance.IsAuthenticated) return;

        ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Waiting;
        UpdateNotchBackground();
        ClosedContent.ShowPredictionWaiting();
        _predictionAudioSending = false;

        // Capture context immediately (before VE window activates)
        _predictionAppName = ScreenCaptureManager.Instance.GetActiveAppName();
        _predictionWindowTitle = ScreenCaptureManager.Instance.GetActiveWindowTitle();

        Task.Run(async () =>
        {
            // Capture screenshot in background
            _predictionScreenshot = ScreenCaptureManager.Instance.CaptureActiveWindow();

            // Ensure WebSocket is connected
            var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
            if (client == null || !client.IsConnected)
            {
                await WebSocket.WebSocketRegistry.Instance.ConnectUnifiedAudioTransport();
                client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
            }

            if (client == null)
            {
                Dispatcher.Invoke(() =>
                {
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Error;
                    ViewCoordinator.Instance.ErrorMessage = "Connection failed";
                    ClosedContent.ShowError("Connection failed");
                });
                return;
            }

            // Reset accumulated text for new prediction
            client.ResetAccumulatedText();

            // Subscribe to events (remove old handlers first to avoid duplicates)
            client.OnPredictionStreaming -= OnPredictionStreamingHandler;
            client.OnPredictionComplete -= OnPredictionCompleteHandler;
            client.OnError -= OnPredictionErrorHandler;

            client.OnPredictionStreaming += OnPredictionStreamingHandler;
            client.OnPredictionComplete += OnPredictionCompleteHandler;
            client.OnError += OnPredictionErrorHandler;

            // Start audio capture and stream chunks
            AudioService.Instance.OnAudioDataAvailable += OnPredictionAudioData;
            AudioService.Instance.StartCapture();

            // Wait 200ms before enabling chunk sending (matches macOS quick-release behavior)
            await Task.Delay(200);
            _predictionAudioSending = true;
        });
    }

    private void OnPredictionStreamingHandler(object? sender, string text)
    {
        Dispatcher.Invoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Streaming;
            ViewCoordinator.Instance.PredictionText = text;
            ClosedContent.ShowPredictionStreaming(text);
        });
    }

    private void OnPredictionCompleteHandler(object? sender, WebSocket.PredictionResult result)
    {
        Dispatcher.Invoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Success;
            ClipboardManager.Instance.PasteText(result.Text);
            ClosedContent.ShowPredictionSuccess(result.Text);
            Services.PredictionFeedbackService.Instance.OnPredictionSuccess(result.Id);

            // Auto-dismiss after 4s (matches macOS)
            Task.Delay(4000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Inactive;
                    ClosedContent.ResetToIdle();
                    UpdateNotchBackground();
                });
            });
        });
    }

    private void OnPredictionErrorHandler(object? sender, string error)
    {
        Dispatcher.Invoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Error;
            ViewCoordinator.Instance.ErrorMessage = error;
            ClosedContent.ShowError(error);

            Task.Delay(3000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Inactive;
                    ClosedContent.ResetToIdle();
                    UpdateNotchBackground();
                });
            });
        });
    }

    private void OnPredictionAudioData(object? sender, byte[] data)
    {
        if (!_predictionAudioSending) return;
        var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
        _ = client?.SendAudioChunk(data);
    }

    private void OnPredictionReleased()
    {
        // Stop audio capture
        AudioService.Instance.OnAudioDataAvailable -= OnPredictionAudioData;
        AudioService.Instance.StopCapture();

        // Send end payload with screenshot + metadata
        Task.Run(async () =>
        {
            var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
            if (client != null)
            {
                await client.SendEndPayload(
                    audioCompleted: _predictionAudioSending,
                    screenshot: _predictionScreenshot,
                    appName: _predictionAppName,
                    windowTitle: _predictionWindowTitle);
            }
        });
    }

    // Dictation flow
    private void OnDictationTriggered()
    {
        ViewCoordinator.Instance.DictationState = Services.DictationState.Waiting;
        UpdateNotchBackground();
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