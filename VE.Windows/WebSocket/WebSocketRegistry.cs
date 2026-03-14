using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Services;

namespace VE.Windows.WebSocket;

/// <summary>
/// Registry that owns and initializes all WebSocket transports and clients.
/// Equivalent to macOS WebSocketRegistry.
/// </summary>
public sealed class WebSocketRegistry : IDisposable
{
    public static WebSocketRegistry Instance { get; } = new();

    // Transports
    private WebSocketTransport? _unifiedAudioTransport;
    private WebSocketTransport? _dictationTransport;
    private WebSocketTransport? _multiAgentTransport;
    private WebSocketTransport? _voiceToTextTransport;
    private WebSocketTransport? _meetingTransport;

    // Clients
    public UnifiedAudioSocketClient? UnifiedAudioClient { get; private set; }
    public VoiceToTextSocketClient? VoiceToTextClient { get; private set; }
    public MultiAgentSocketClient? MultiAgentClient { get; private set; }

    private WebSocketRegistry() { }

    public async Task ConnectUnifiedAudioTransport()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("unified_audio_ws_api");
        if (baseUrl == null) return;

        var tenantId = AuthManager.Instance.Storage.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            FileLogger.Instance.Warning("WebSocketRegistry", "Tenant ID not available for unified audio WebSocket URL");
            return;
        }

        // Generate a unique session ID (hex string like macOS BSON ObjectId)
        var sessionId = Guid.NewGuid().ToString("N").Substring(0, 24);

        // Construct URL: wss://cursor-intelligence.{region}.ve.ai/{tenantId}/{sessionId}/invoke-predictor
        var url = $"{baseUrl}/{tenantId}/{sessionId}/invoke-predictor";

        FileLogger.Instance.Info("WebSocketRegistry", $"Connecting unified audio to: {url}");

        _unifiedAudioTransport?.Dispose();
        _unifiedAudioTransport = new WebSocketTransport(url);
        UnifiedAudioClient = new UnifiedAudioSocketClient(_unifiedAudioTransport);

        await _unifiedAudioTransport.ConnectAsync();
        FileLogger.Instance.Info("WebSocketRegistry", "Unified audio transport connected");
    }

    public async Task ConnectDictationTransport()
    {
        var url = BaseURLService.Instance.GetBaseUrl("dictation_ws_api");
        if (url == null) return;

        _dictationTransport?.Dispose();
        _dictationTransport = new WebSocketTransport(url);
        VoiceToTextClient = new VoiceToTextSocketClient(_dictationTransport);

        await _dictationTransport.ConnectAsync();
        FileLogger.Instance.Info("WebSocketRegistry", "Dictation transport connected");
    }

    public async Task ConnectMultiAgentTransport()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("chat_ws_api");
        if (baseUrl == null) return;

        var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
        if (string.IsNullOrEmpty(workspaceId))
        {
            FileLogger.Instance.Warning("WebSocketRegistry", "Workspace ID not available for multi-agent WebSocket URL");
            return;
        }

        var token = AuthManager.Instance.Storage.UserToken;
        if (string.IsNullOrEmpty(token))
        {
            FileLogger.Instance.Warning("WebSocketRegistry", "Token not available for multi-agent WebSocket URL");
            return;
        }

        var sessionId = Guid.NewGuid().ToString("N").Substring(0, 24);
        var encodedToken = Uri.EscapeDataString(token);
        var url = $"{baseUrl}/{workspaceId}/{sessionId}/multi_agent_chat_streaming?token={encodedToken}";

        FileLogger.Instance.Info("WebSocketRegistry", $"Connecting multi-agent to: {baseUrl}/.../{sessionId}/multi_agent_chat_streaming");

        _multiAgentTransport?.Dispose();
        _multiAgentTransport = new WebSocketTransport(url);
        MultiAgentClient = new MultiAgentSocketClient(_multiAgentTransport);

        await _multiAgentTransport.ConnectAsync();
        FileLogger.Instance.Info("WebSocketRegistry", "Multi-agent transport connected");
    }

    public MeetingSocketClient? MeetingClient { get; private set; }

    public async Task ConnectMeetingTransport(string meetingId)
    {
        var token = AuthManager.Instance.Storage.UserToken;
        if (string.IsNullOrEmpty(token))
        {
            FileLogger.Instance.Warning("WebSocketRegistry", "Token not available for meeting WebSocket URL");
            return;
        }

        var encodedToken = Uri.EscapeDataString(token);
        // macOS uses: wss://meetings.us-east-1.ve.ai/frontend/ws/{meetingId}?token={encodedToken}
        var url = $"wss://meetings.us-east-1.ve.ai/frontend/ws/{meetingId}?token={encodedToken}";

        FileLogger.Instance.Info("WebSocketRegistry", $"Connecting meeting transport for meeting: {meetingId}");

        _meetingTransport?.Dispose();
        _meetingTransport = new WebSocketTransport(url);
        MeetingClient = new MeetingSocketClient(_meetingTransport);

        await _meetingTransport.ConnectAsync();
        FileLogger.Instance.Info("WebSocketRegistry", "Meeting transport connected");
    }

    public async Task DisconnectMeetingTransport()
    {
        _meetingTransport?.Dispose();
        _meetingTransport = null;
        MeetingClient = null;
    }

    public void DisconnectAll()
    {
        FileLogger.Instance.Info("WebSocketRegistry", "Disconnecting all transports...");
        _unifiedAudioTransport?.Dispose();
        _dictationTransport?.Dispose();
        _multiAgentTransport?.Dispose();
        _voiceToTextTransport?.Dispose();
        _meetingTransport?.Dispose();

        _unifiedAudioTransport = null;
        _dictationTransport = null;
        _multiAgentTransport = null;
        _voiceToTextTransport = null;
        _meetingTransport = null;

        UnifiedAudioClient = null;
        VoiceToTextClient = null;
        MultiAgentClient = null;
        MeetingClient = null;
    }

    public async Task VerifyAllConnections()
    {
        if (_unifiedAudioTransport != null)
            await _unifiedAudioTransport.VerifyConnectionHealth();
        if (_dictationTransport != null)
            await _dictationTransport.VerifyConnectionHealth();
        if (_multiAgentTransport != null)
            await _multiAgentTransport.VerifyConnectionHealth();
    }

    public void Dispose()
    {
        DisconnectAll();
    }
}
