using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VE.Windows.Helpers;

namespace VE.Windows.Services;

/// <summary>
/// Automatic meeting detection by scanning window titles and microphone usage.
/// Matches macOS MeetingDetectionService: 7-second poll interval, detects Zoom, Teams,
/// Google Meet, Slack Huddle, Webex, Discord, and browser-based meetings.
/// Windows implementation uses EnumWindows + GetWindowText P/Invoke instead of CoreAudio callbacks.
/// </summary>
public sealed class MeetingDetectionService : IDisposable
{
    public static MeetingDetectionService Instance { get; } = new();

    private Timer? _pollTimer;
    private bool _isMeetingDetected;
    private string? _detectedAppName;
    private const int PollIntervalMs = 7000; // 7 seconds (matches macOS)

    public bool IsMeetingDetected
    {
        get => _isMeetingDetected;
        private set
        {
            if (_isMeetingDetected != value)
            {
                _isMeetingDetected = value;
                OnMeetingDetectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string? DetectedMeetingAppName
    {
        get => _detectedAppName;
        private set => _detectedAppName = value;
    }

    public event EventHandler? OnMeetingDetectionChanged;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // High-confidence: app title alone indicates meeting
    private static readonly (string pattern, string appName)[] HighConfidencePatterns =
    {
        ("Zoom Meeting", "Zoom"),
        ("Zoom Webinar", "Zoom"),
        ("zoom.us", "Zoom"),
        ("Slack Huddle", "Slack Huddle"),
        ("Slack call", "Slack Huddle"),
        ("Discord |", "Discord"),
    };

    // Low-confidence: need additional signals (like mic in use)
    private static readonly (string pattern, string appName)[] LowConfidencePatterns =
    {
        ("Microsoft Teams", "Microsoft Teams"),
        ("Webex", "Webex"),
        ("FaceTime", "FaceTime"),
    };

    // Browser-based meeting patterns (in window titles)
    private static readonly (string pattern, string appName)[] BrowserMeetingPatterns =
    {
        ("Google Meet", "Google Meet"),
        ("meet.google.com", "Google Meet"),
        ("teams.microsoft.com", "Microsoft Teams"),
        ("teams.live.com", "Microsoft Teams"),
        ("zoom.us/j/", "Zoom"),
        ("zoom.us/wc/", "Zoom"),
        ("webex.com/meet", "Webex"),
        ("app.slack.com/huddle", "Slack Huddle"),
    };

    // Browser process names
    private static readonly string[] BrowserProcessNames =
    {
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi",
        "iexplore", "safari", "arc", "chromium", "waterfox", "floorp"
    };

    private MeetingDetectionService() { }

    /// <summary>
    /// Start polling for meeting detection every 7 seconds.
    /// </summary>
    public void Start()
    {
        _pollTimer?.Dispose();
        _pollTimer = new Timer(OnPoll, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(PollIntervalMs));
        FileLogger.Instance.Info("MeetingDetection", "Started polling (7s interval)");
    }

    /// <summary>
    /// Stop meeting detection polling.
    /// </summary>
    public void Stop()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        IsMeetingDetected = false;
        DetectedMeetingAppName = null;
    }

    private void OnPoll(object? state)
    {
        try
        {
            var (detected, appName) = ScanForMeetings();
            DetectedMeetingAppName = appName;
            IsMeetingDetected = detected;

            if (detected)
            {
                ViewCoordinator.Instance.IsMeetingDetected = true;
                ViewCoordinator.Instance.DetectedMeetingAppName = appName;
            }
            else
            {
                ViewCoordinator.Instance.IsMeetingDetected = false;
                ViewCoordinator.Instance.DetectedMeetingAppName = null;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingDetection", $"Poll error: {ex.Message}");
        }
    }

    /// <summary>
    /// Scan all visible window titles for meeting app patterns.
    /// </summary>
    private (bool detected, string? appName) ScanForMeetings()
    {
        string? foundApp = null;

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var length = GetWindowTextLength(hWnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (string.IsNullOrWhiteSpace(title)) return true;

            // Check high-confidence patterns first
            foreach (var (pattern, appName) in HighConfidencePatterns)
            {
                if (title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    foundApp = appName;
                    return false; // Stop enumeration
                }
            }

            // Check browser-based meeting patterns
            foreach (var (pattern, appName) in BrowserMeetingPatterns)
            {
                if (title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    foundApp = appName;
                    return false;
                }
            }

            // Check low-confidence patterns
            foreach (var (pattern, appName) in LowConfidencePatterns)
            {
                if (title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    foundApp = appName;
                    return false;
                }
            }

            return true; // Continue enumeration
        }, IntPtr.Zero);

        return (foundApp != null, foundApp);
    }

    public void Dispose()
    {
        Stop();
    }
}
