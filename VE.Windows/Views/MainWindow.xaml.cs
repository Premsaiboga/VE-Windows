using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.ViewModels;

namespace VE.Windows.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private bool _isHovering;
    private CancellationTokenSource? _hoverCts;

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

        // Auth state changes
        AuthManager.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AuthManager.AuthState))
            {
                Dispatcher.Invoke(UpdateNotchBackground);
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
            // Only open on click when not authenticated
            if (!AuthManager.Instance.IsAuthenticated)
            {
                AnimateOpen();
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
        if (!AuthManager.Instance.IsAuthenticated && !_vm.IsOpen)
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

        if (!AuthManager.Instance.IsAuthenticated && _vm.IsOpen)
        {
            Task.Delay(150).ContinueWith(_ =>
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

    // Prediction flow
    private void OnPredictionTriggered()
    {
        ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Waiting;
        UpdateNotchBackground();
        ClosedContent.ShowPredictionWaiting();

        Task.Run(async () =>
        {
            // Capture screenshot + audio
            var screenshot = ScreenCaptureManager.Instance.CaptureActiveWindow();

            AudioService.Instance.OnAudioDataAvailable += OnPredictionAudioData;
            AudioService.Instance.StartCapture();

            var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
            if (client != null && screenshot != null)
            {
                await client.SendPredictionRequest(Array.Empty<byte>(), screenshot, null);
            }

            client!.OnPredictionReceived += (s, text) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Streaming;
                    ViewCoordinator.Instance.PredictionText = text;
                    ClosedContent.ShowPredictionStreaming(text);
                });
            };

            client.OnPredictionComplete += (s, result) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Success;
                    ClipboardManager.Instance.PasteText(result.Text);
                    ClosedContent.ShowPredictionSuccess(result.Text);
                    Services.PredictionFeedbackService.Instance.OnPredictionSuccess(result.Id);

                    // Auto-dismiss after 3s
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
            };
        });
    }

    private void OnPredictionAudioData(object? sender, byte[] data)
    {
        var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
        _ = client?.SendAudioChunk(data);
    }

    private void OnPredictionReleased()
    {
        AudioService.Instance.OnAudioDataAvailable -= OnPredictionAudioData;
        AudioService.Instance.StopCapture();
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
}