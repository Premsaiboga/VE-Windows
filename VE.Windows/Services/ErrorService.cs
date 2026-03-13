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
/// Centralized error logging to Sentry and Slack webhook.
/// Equivalent to macOS ErrorService.
/// </summary>
public sealed class ErrorService
{
    public static ErrorService Instance { get; } = new();

    private const string SentryDSN = "https://3317a0b1eb30bcefaa9926d5f8db90fd@o4510844418195456.ingest.us.sentry.io/4511019648811008";
    private readonly HttpClient _httpClient = new();

    private ErrorService() { }

    public void ConfigureSentry()
    {
        try
        {
            SentrySdk.Init(options =>
            {
                options.Dsn = SentryDSN;
                options.Environment = "production";
                options.Release = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
                options.AutoSessionTracking = true;
                options.IsGlobalModeEnabled = true;
                options.TracesSampleRate = 0.2;
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

    public void LogMessage(string message, ErrorCategory category, ErrorSeverity severity,
        Dictionary<string, string>? context = null)
    {
        // Log locally
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

        // Send to Sentry
        try
        {
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

    public void LogException(Exception exception, ErrorCategory category, Dictionary<string, string>? context = null)
    {
        FileLogger.Instance.Error(category.ToString(), exception.ToString());

        try
        {
            SentrySdk.CaptureException(exception);
        }
        catch { }
    }

    public async Task Flush()
    {
        try
        {
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(5));
        }
        catch { }
    }
}
