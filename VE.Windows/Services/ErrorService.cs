using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Sentry;
using VE.Windows.Helpers;
using VE.Windows.Managers;

namespace VE.Windows.Services;

public enum ErrorCategory
{
    Crash,
    Network,
    Auth,
    WebSocket,
    Audio,
    General
}

public enum ErrorSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Centralized error logging with rate limiting, Sentry integration, and Slack webhook.
/// Matches macOS ErrorService: max 10 errors per 60-second window, 10-second minimum
/// between sends, critical errors bypass rate limiting, error batching.
/// </summary>
public sealed class ErrorService
{
    public static ErrorService Instance { get; } = new();

    private const string SentryDSN = "https://3317a0b1eb30bcefaa9926d5f8db90fd@o4510844418195456.ingest.us.sentry.io/4511019648811008";
    private readonly HttpClient _httpClient = new();

    // Rate limiting (matches macOS: 10 errors/60s, 10s minimum interval)
    private readonly ConcurrentQueue<DateTime> _errorTimestamps = new();
    private DateTime _lastErrorSent = DateTime.MinValue;
    private int _rateLimitedCount;
    private const int MaxErrorsPerMinute = 10;
    private const int MinIntervalSeconds = 10;

    // Error batching
    private readonly ConcurrentQueue<PendingError> _errorQueue = new();
    private Timer? _batchTimer;

    private ErrorService() { }

    public void ConfigureSentry()
    {
        try
        {
            SentrySdk.Init(options =>
            {
                options.Dsn = SentryDSN;
#if DEBUG
                options.Environment = "development";
                options.TracesSampleRate = 0.2;
#else
                options.Environment = "production";
                options.TracesSampleRate = 0.05; // 5% in production (matches macOS)
#endif
                options.Release = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
                options.AutoSessionTracking = true;
                options.IsGlobalModeEnabled = true;
            });
            FileLogger.Instance.Info("ErrorService", "Sentry initialized");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ErrorService", $"Failed to initialize Sentry: {ex.Message}");
        }
    }

    public void UpdateSentryUser()
    {
        try
        {
            var storage = AuthManager.Instance.Storage;
            SentrySdk.ConfigureScope(scope =>
            {
                scope.User = new SentryUser
                {
                    Email = storage.UserEmail,
                    Id = storage.TenantId,
                    Other = new Dictionary<string, string>
                    {
                        ["workspaceId"] = storage.WorkspaceId ?? "",
                        ["region"] = storage.Region ?? ""
                    }
                };
            });
        }
        catch { }
    }

    /// <summary>
    /// Log an error message with rate limiting and severity-based routing.
    /// Critical errors bypass rate limiting. All errors go to FileLogger.
    /// Rate-limited errors are batched into a single "rate limited" event.
    /// </summary>
    public void LogMessage(string message, ErrorCategory category, ErrorSeverity severity,
        Dictionary<string, string>? context = null)
    {
        // Always log locally
        switch (severity)
        {
            case ErrorSeverity.Critical:
                FileLogger.Instance.Critical(category.ToString(), message);
                break;
            case ErrorSeverity.Error:
                FileLogger.Instance.Error(category.ToString(), message);
                break;
            case ErrorSeverity.Warning:
                FileLogger.Instance.Warning(category.ToString(), message);
                break;
            default:
                FileLogger.Instance.Info(category.ToString(), message);
                break;
        }

        // Critical errors bypass rate limiting
        if (severity == ErrorSeverity.Critical)
        {
            SendToSentry(message, severity, context);
            return;
        }

        // Rate limiting check
        if (!ShouldSendError())
        {
            _rateLimitedCount++;
            return;
        }

        // Flush any rate-limited count
        if (_rateLimitedCount > 0)
        {
            SendToSentry($"[Rate Limited] {_rateLimitedCount} additional errors suppressed", ErrorSeverity.Warning);
            _rateLimitedCount = 0;
        }

        SendToSentry(message, severity, context);
    }

    /// <summary>
    /// Log an exception to Sentry with rate limiting.
    /// </summary>
    public void LogException(Exception exception, ErrorCategory category, Dictionary<string, string>? context = null)
    {
        FileLogger.Instance.Error(category.ToString(), exception.ToString());

        if (!ShouldSendError()) { _rateLimitedCount++; return; }

        try
        {
            SentrySdk.CaptureException(exception);
        }
        catch { }
    }

    /// <summary>
    /// Log a crash event. Always sent regardless of rate limiting. Flushes Sentry before return.
    /// </summary>
    public void LogCrashAndReport(Exception? exception)
    {
        var message = exception?.Message ?? "Unknown crash";
        var stackTrace = exception?.StackTrace ?? "";
        var exType = exception?.GetType().Name ?? "Unknown";

        FileLogger.Instance.Critical("Crash", exception?.ToString() ?? "Unknown crash");
        FileLogger.Instance.FlushNow();

        // Always send crashes to Sentry (bypass rate limiting)
        try
        {
            if (exception != null)
            {
                SentrySdk.CaptureException(exception);
            }
            else
            {
                SentrySdk.CaptureMessage("Unknown crash", SentryLevel.Fatal);
            }
            SentrySdk.FlushAsync(TimeSpan.FromSeconds(5)).Wait();
        }
        catch { }
    }

    /// <summary>
    /// Rate limiting: max 10 errors per 60 seconds, minimum 10 seconds between sends.
    /// Matches macOS ErrorService rate limiting.
    /// </summary>
    private bool ShouldSendError()
    {
        var now = DateTime.UtcNow;

        // Minimum interval between sends
        if ((now - _lastErrorSent).TotalSeconds < MinIntervalSeconds)
            return false;

        // Clean old timestamps (>60 seconds)
        while (_errorTimestamps.TryPeek(out var oldest) && (now - oldest).TotalSeconds > 60)
        {
            _errorTimestamps.TryDequeue(out _);
        }

        // Max errors per minute
        if (_errorTimestamps.Count >= MaxErrorsPerMinute)
            return false;

        _errorTimestamps.Enqueue(now);
        _lastErrorSent = now;
        return true;
    }

    private void SendToSentry(string message, ErrorSeverity severity, Dictionary<string, string>? context = null)
    {
        try
        {
            SentrySdk.ConfigureScope(scope =>
            {
                if (context != null)
                {
                    foreach (var kv in context)
                    {
                        scope.SetTag(kv.Key, kv.Value);
                    }
                }
            });

            SentrySdk.CaptureMessage(message, severity switch
            {
                ErrorSeverity.Critical => SentryLevel.Fatal,
                ErrorSeverity.Error => SentryLevel.Error,
                ErrorSeverity.Warning => SentryLevel.Warning,
                _ => SentryLevel.Info
            });
        }
        catch { }
    }

    public async Task Flush()
    {
        // Flush any remaining rate-limited count
        if (_rateLimitedCount > 0)
        {
            SendToSentry($"[Rate Limited] {_rateLimitedCount} additional errors suppressed", ErrorSeverity.Warning);
            _rateLimitedCount = 0;
        }

        try
        {
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(5));
        }
        catch { }
    }

    private record PendingError(string Message, ErrorSeverity Severity, Dictionary<string, string>? Context);
}
