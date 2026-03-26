using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using VE.Windows.Helpers;
using VE.Windows.Infrastructure;
using VE.Windows.Managers;
using VE.Windows.Models;
using VE.Windows.Services;
using VE.Windows.Theme;
using VE.Windows.Views;
using VE.Windows.WebSocket;

namespace VE.Windows;

public partial class App : Application
{
    private static Mutex? _mutex;
    private TaskbarIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private CancellationTokenSource? _pipeCts;
    private const string PipeName = "VE.Windows.SingleInstance.Pipe";

    /// <summary>
    /// DI service provider — available for resolving interfaces throughout the app.
    /// Singletons are registered pointing to existing .Instance properties for backward compatibility.
    /// </summary>
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance check
        const string mutexName = "VE.Windows.SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is running — forward args via named pipe and exit
            ForwardArgsToRunningInstance(e.Args);
            Current.Shutdown();
            return;
        }

        base.OnStartup(e);

        // Bootstrap DI container
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddVEServices();
        Services = serviceCollection.BuildServiceProvider();

        // Initialize logging
        FileLogger.Instance.Info("App", "Application starting");

        // Initialize Sentry
        ErrorService.Instance.ConfigureSentry();

        // Setup crash handler
        SetupCrashHandler();

        // Initialize core services — theme must apply resources after App.Resources are loaded
        ThemeManager.Instance.Initialize();
        _ = AuthManager.Instance;
        _ = BaseURLService.Instance;
        _ = NetworkService.Instance;

        // Register ve:// protocol handler (verifies path on every launch)
        ProtocolHandler.RegisterProtocol();

        // Setup auto-start on first launch
        var settings = SettingsManager.Instance;
        if (!settings.Get("HasLaunchedBefore", false))
        {
            AutoStartHelper.IsEnabled = true;
            settings.Set("HasLaunchedBefore", true);
        }

        // Verify auto-start registry matches current exe path (handles app moved/updated)
        AutoStartHelper.VerifyRegistryPath();

        // Clean up any pending installers from previous sessions
        UpdateService.Instance.CheckPendingInstall();

        // Create system tray icon
        CreateNotifyIcon();

        // Create and show main floating window
        _mainWindow = new MainWindow();
        _mainWindow.Show();

        // Handle URI activation (ve:// protocol)
        HandleCommandLineArgs(e.Args);

        // Start listening for args from other instances (OAuth callbacks)
        StartPipeServer();

        // Pre-warm services if authenticated
        if (AuthManager.Instance.IsAuthenticated)
        {
            Task.Run(async () =>
            {
                await WebSocketRegistry.Instance.ConnectUnifiedAudioTransport();
            });
        }

        // Initialize keyboard hooks
        KeyboardHookManager.Instance.Start();

        // App lifecycle observers (matches macOS resignActive / didBecomeActive)
        Activated += async (s, e) => await WebSocketRegistry.Instance.OnAppActivated();
        Deactivated += (s, e) => WebSocketRegistry.Instance.OnAppDeactivated();

        FileLogger.Instance.Info("App", "Application started successfully");
    }

    /// <summary>
    /// Send command-line args to the already-running instance via named pipe.
    /// This is how OAuth ve:// callbacks reach the first instance.
    /// </summary>
    private static void ForwardArgsToRunningInstance(string[] args)
    {
        if (args.Length == 0) return;

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000); // 2s timeout
            using var writer = new StreamWriter(client);
            writer.WriteLine(args[0]);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to forward args: {ex.Message}");
        }
    }

    /// <summary>
    /// Listen for args from new instances (OAuth callbacks) via named pipe.
    /// Auto-restarts on crash with backoff. Handles broken pipe edge cases.
    /// </summary>
    private void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var ct = _pipeCts.Token;
        var consecutiveErrors = 0;

        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);
                    consecutiveErrors = 0; // Reset on successful connection

                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync();

                    if (!string.IsNullOrEmpty(message))
                    {
                        FileLogger.Instance.Info("App", $"Received from pipe: {message}");
                        await Dispatcher.InvokeAsync(() => HandleCommandLineArgs(new[] { message }));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex) when (ex.Message.Contains("broken"))
                {
                    // Broken pipe — client disconnected before sending. Normal, just restart.
                    FileLogger.Instance.Debug("App", "Pipe client disconnected (broken pipe)");
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    FileLogger.Instance.Error("App", $"Pipe server error ({consecutiveErrors}): {ex.Message}");

                    // Exponential backoff: 500ms, 1s, 2s, 4s, max 10s
                    var delay = Math.Min(500 * (1 << Math.Min(consecutiveErrors - 1, 4)), 10_000);
                    await Task.Delay(delay, ct);
                }
            }
        }, ct);
    }

    private void CreateNotifyIcon()
    {
        _notifyIcon = new TaskbarIcon
        {
            ToolTipText = "VE AI Desktop",
            MenuActivation = PopupActivationMode.RightClick
        };

        // Try to load icon from resources, fallback to default
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/ve-icon.ico", UriKind.Absolute);
            var iconStream = GetResourceStream(iconUri)?.Stream;
            if (iconStream != null)
            {
                _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warning("App", $"Failed to load tray icon: {ex.Message}");
        }

        _notifyIcon.ContextMenu = CreateContextMenu();
        _notifyIcon.TrayMouseDoubleClick += (s, e) =>
        {
            _mainWindow?.Activate();
        };
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var isAuth = AuthManager.Instance.IsAuthenticated;

        AddMenuItem(menu, "Open Chat", () =>
        {
            if (!AuthManager.Instance.IsAuthenticated) return;
            ViewCoordinator.Instance.SelectedNavigationTab = NavigationTab.Chat;
            ShowSettingsWindow("apps");
        }, isAuth);

        AddMenuItem(menu, "Open Work", () =>
        {
            if (!AuthManager.Instance.IsAuthenticated) return;
            ViewCoordinator.Instance.SelectedNavigationTab = NavigationTab.Home;
            ShowSettingsWindow("apps");
        }, isAuth);

        AddMenuItem(menu, "Quick Note", () =>
        {
            if (!AuthManager.Instance.IsAuthenticated) return;
            Task.Run(async () => await MeetingService.Instance.StartMeeting());
        }, isAuth);

        menu.Items.Add(new System.Windows.Controls.Separator());

        AddMenuItem(menu, "Settings", () =>
        {
            if (!AuthManager.Instance.IsAuthenticated) return;
            ViewCoordinator.Instance.SelectedNavigationTab = NavigationTab.Home;
            ShowSettingsWindow("settings");
        }, isAuth);

        AddMenuItem(menu, "Shortcuts", () =>
        {
            if (!AuthManager.Instance.IsAuthenticated) return;
            ShowSettingsWindow("shortcuts");
        }, isAuth);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Microphone submenu
        var micMenu = new System.Windows.Controls.MenuItem { Header = "Microphone" };
        var micDefault = new System.Windows.Controls.MenuItem { Header = "System Default", IsCheckable = true, IsChecked = true };
        micDefault.Click += (s, e) => SettingsManager.Instance.Set("SelectedMicrophoneUID", "system-default");
        micMenu.Items.Add(micDefault);
        menu.Items.Add(micMenu);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionItem = new System.Windows.Controls.MenuItem
        {
            Header = $"Version {version?.ToString(3) ?? "1.0.0"}",
            IsEnabled = false
        };
        menu.Items.Add(versionItem);

        AddMenuItem(menu, "Check for Updates", () =>
        {
            Task.Run(async () => await UpdateService.Instance.CheckForUpdates());
        });

        menu.Items.Add(new System.Windows.Controls.Separator());

        AddMenuItem(menu, "Help Center", () =>
        {
            Models.AppURLs.Open(Models.AppURLType.HelpCenter);
        });

        AddMenuItem(menu, "Talk to Support", () =>
        {
            Models.AppURLs.Open(Models.AppURLType.Support);
        });

        menu.Items.Add(new System.Windows.Controls.Separator());

        AddMenuItem(menu, "Restart VE", () =>
        {
            ApplicationRelauncher.Restart();
        });

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit VE" };
        quitItem.Click += (s, e) =>
        {
            Current.Shutdown();
        };
        menu.Items.Add(quitItem);

        return menu;
    }

    private static void AddMenuItem(System.Windows.Controls.ContextMenu menu, string header, Action action, bool isEnabled = true)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header, IsEnabled = isEnabled };
        item.Click += (s, e) => action();
        menu.Items.Add(item);
    }

    private void ShowSettingsWindow(string section)
    {
        var settingsWindow = Current.Windows.OfType<Views.Settings.SettingsWindow>().FirstOrDefault();
        if (settingsWindow == null)
        {
            settingsWindow = new Views.Settings.SettingsWindow();
        }
        settingsWindow.NavigateToSection(section);
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    private void HandleCommandLineArgs(string[] args)
    {
        if (args.Length > 0)
        {
            var uri = args[0];
            if (uri.StartsWith("ve://"))
            {
                FileLogger.Instance.Info("App", $"Handling URI: {uri}");
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await ProtocolHandler.HandleUri(uri);

                    // After successful auth, activate the main window
                    if (AuthManager.Instance.IsAuthenticated && _mainWindow != null)
                    {
                        _mainWindow.Activate();
                        _mainWindow.Topmost = true;
                        _mainWindow.Topmost = false;
                    }
                });
            }
        }
    }

    /// <summary>
    /// Register three global exception handlers matching macOS NSSetUncaughtExceptionHandler.
    /// Logs to FileLogger, sends to Sentry with device context, flushes before termination.
    /// </summary>
    private void SetupCrashHandler()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            ErrorService.Instance.LogCrashAndReport(args.ExceptionObject as Exception);
        };

        Current.DispatcherUnhandledException += (sender, args) =>
        {
            ErrorService.Instance.LogCrashAndReport(args.Exception);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            ErrorService.Instance.LogCrashAndReport(args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FileLogger.Instance.Info("App", "Application shutting down");
        _pipeCts?.Cancel();
        KeyboardHookManager.Instance.Stop();
        WebSocketRegistry.Instance.DisconnectAll();
        TokenRefreshService.Instance.Dispose();
        _notifyIcon?.Dispose();
        Services?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
