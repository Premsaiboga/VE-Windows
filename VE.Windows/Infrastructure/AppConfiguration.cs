using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;

namespace VE.Windows.Infrastructure;

/// <summary>
/// Centralized application configuration.
/// Reads from embedded defaults with optional override from %AppData%/VE/config.json.
/// Replaces hardcoded values scattered across ErrorService, BaseURLService, WebSocketRegistry.
/// </summary>
public interface IAppConfiguration
{
    string SentryDSN { get; }
    string Environment { get; }
    string MeetingWebSocketBaseUrl { get; }
    string SummaryWebSocketBaseUrl { get; }
    string DefaultRegion { get; }
    int MaxWebSocketRetries { get; }
    int MaxTokenRefreshFailures { get; }
    int AudioBufferMaxChunks { get; }
    int MaxLiveTranscriptions { get; }
    bool IsFeatureEnabled(string featureName);
}

public sealed class AppConfiguration : IAppConfiguration
{
    private static AppConfiguration? _instance;
    public static AppConfiguration Instance => _instance ??= new AppConfiguration();

    private readonly Dictionary<string, bool> _featureFlags;
    private readonly JObject _config;

    public string SentryDSN { get; }
    public string Environment { get; }
    public string MeetingWebSocketBaseUrl { get; }
    public string SummaryWebSocketBaseUrl { get; }
    public string DefaultRegion { get; }
    public int MaxWebSocketRetries { get; }
    public int MaxTokenRefreshFailures { get; }
    public int AudioBufferMaxChunks { get; }
    public int MaxLiveTranscriptions { get; }

    private AppConfiguration()
    {
        _config = LoadConfig();
        _featureFlags = new Dictionary<string, bool>();

        // Load with defaults — config.json overrides
#if DEBUG
        Environment = GetString("environment", "development");
#else
        Environment = GetString("environment", "production");
#endif
        SentryDSN = GetString("sentryDsn", "");
        MeetingWebSocketBaseUrl = GetString("meetingWebSocketBaseUrl", "wss://meetings.us-east-1.ve.ai");
        SummaryWebSocketBaseUrl = GetString("summaryWebSocketBaseUrl", "wss://meetings.us-east-1.ve.ai");
        DefaultRegion = GetString("defaultRegion", "us-east-1");
        MaxWebSocketRetries = GetInt("maxWebSocketRetries", 10);
        MaxTokenRefreshFailures = GetInt("maxTokenRefreshFailures", 3);
        AudioBufferMaxChunks = GetInt("audioBufferMaxChunks", 500);
        MaxLiveTranscriptions = GetInt("maxLiveTranscriptions", 5000);

        // Load feature flags
        var flags = _config["featureFlags"] as JObject;
        if (flags != null)
        {
            foreach (var kv in flags)
            {
                if (kv.Value?.Type == JTokenType.Boolean)
                    _featureFlags[kv.Key] = kv.Value.Value<bool>();
            }
        }
    }

    public bool IsFeatureEnabled(string featureName)
    {
        return _featureFlags.TryGetValue(featureName, out var enabled) && enabled;
    }

    private static JObject LoadConfig()
    {
        try
        {
            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var configPath = Path.Combine(appData, "VE", "config.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                return JObject.Parse(json);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warning("AppConfig", $"Failed to load config override: {ex.Message}");
        }

        return new JObject();
    }

    private string GetString(string key, string defaultValue)
    {
        return _config[key]?.ToString() ?? defaultValue;
    }

    private int GetInt(string key, int defaultValue)
    {
        var val = _config[key];
        if (val != null && val.Type == JTokenType.Integer)
            return val.Value<int>();
        return defaultValue;
    }
}
