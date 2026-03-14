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

    private void OnPredictionTriggered()
    {
        if (!AuthManager.Instance.IsAuthenticated) return;

        ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Waiting;
        ClosedContent.ShowPredictionWaiting();
        _predictionAudioSending = false;

        _predictionAppName = ScreenCaptureManager.Instance.GetActiveAppName();
        _predictionWindowTitle = ScreenCaptureManager.Instance.GetActiveWindowTitle();

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
                Dispatcher.Invoke(() =>
                {
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Error;
                    ViewCoordinator.Instance.ErrorMessage = "Connection failed";
                    ClosedContent.ShowError("Connection failed");
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

            // Start audio capture immediately
            _predictionAudioSending = true;
            AudioService.Instance.OnAudioDataAvailable += OnPredictionAudioData;
            AudioService.Instance.StartCapture();
        });
    }

    private void OnPredictionStreamingHandler(object? sender, string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Streaming;
            ViewCoordinator.Instance.PredictionText = text;
            // Show streaming text in notch for voice predictions
            ClosedContent.ShowPredictionStreaming(text);
        });
    }

    private void OnPredictionCompleteHandler(object? sender, WebSocket.PredictionResult result)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Success;
            ClipboardManager.Instance.PasteText(result.Text);
            ClosedContent.ShowPredictionSuccess(result.Text);
            Services.PredictionFeedbackService.Instance.OnPredictionSuccess(result.Id);

            Task.Delay(4000).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Inactive;
                    ClosedContent.ResetToIdle();
                });
            });
        });
    }

    private void OnPredictionErrorHandler(object? sender, string error)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Error;
            ViewCoordinator.Instance.ErrorMessage = error;
            ClosedContent.ShowError(error); // Auto-dismisses after 3s
        });
    }

    private void OnPredictionAudioData(object? sender, byte[] data)
    {
        if (!_predictionAudioSending) return;
        var client = WebSocket.WebSocketRegistry.Instance.UnifiedAudioClient;
        _ = client?.SendAudioChunk(data);
    }

    /// <summary>
    /// Quick tap prediction: screenshot-only, no audio.
    /// Don't show streaming text in the notch - just show "Predicting" then "Pasted".
    /// </summary>
    private void OnPredictionTapped()
    {
        if (!AuthManager.Instance.IsAuthenticated) return;

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
                    ClosedContent.ShowError("Connection failed");
                });
                return;
            }

            client.ResetAccumulatedText();

            // For tap prediction: only subscribe to complete and error (no streaming display)
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
        Dispatcher.BeginInvoke(() =>
        {
            ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Success;
            ClipboardManager.Instance.PasteText(result.Text);
            ClosedContent.ShowPredictionSuccess(result.Text);
            Services.PredictionFeedbackService.Instance.OnPredictionSuccess(result.Id);

            Task.Delay(4000).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ViewCoordinator.Instance.CombinedPredictionState = CombinedPredictionState.Inactive;
                    ClosedContent.ResetToIdle();
                });
            });
        });
    }

    private void OnPredictionReleased()
    {
        AudioService.Instance.OnAudioDataAvailable -= OnPredictionAudioData;
        AudioService.Instance.StopCapture();

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
