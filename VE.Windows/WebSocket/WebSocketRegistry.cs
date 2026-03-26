using Microsoft.Win32;
using VE.Windows.Helpers;
using VE.Windows.Infrastructure;
using VE.Windows.Managers;
using VE.Windows.Services;

namespace VE.Windows.WebSocket;

/// <summary>
/// Registry that owns all WebSocket transports and clients.
/// Handles sleep/wake reconnection, pending transport pattern for non-disruptive
/// token refresh, and app lifecycle management.
/// Matches macOS WebSocketRegistry: pending transport swap, lifecycle observers,
/// sleep/wake handling.
/// </summary>
public sealed class WebSocketRegistry : IWebSocketRegistry
{
    public static WebSocketRegistry Instance { get; } = new();

    // Active transports
    private WebSocketTransport? _unifiedAudioTransport;
    private WebSocketTransport? _dictationTransport;
    private WebSocketTransport? _multiAgentTransport;
    private WebSocketTransport? _voiceToTextTransport;
    private WebSocketTransport? _meetingTransport;
    private WebSocketTransport? _summaryTransport;

    // Pending transport pattern (matches macOS): new transport with fresh token,
    // swapped in after active operation completes
    private WebSocketTransport? _pendingUnifiedAudioTransport;
    private WebSocketTransport? _oldUnifiedAudioTransport;

    // Clients
    public UnifiedAudioSocketClient? UnifiedAudioClient { get; private set; }
    public VoiceToTextSocketClient? VoiceToTextClient { get; private set; }
    public MultiAgentSocketClient? MultiAgentClient { get; private set; }
    public MeetingSocketClient? MeetingClient { get; private set; }
    public SummarySocketClient? SummaryClient { get; private set; }

    // Expose transport for direct access
    public WebSocketTransport? MultiAgentTransport => _multiAgentTransport;

    // App lifecycle tracking
    private DateTime? _deactivatedAt;
    private Timer? _deactivationTimer;
    private const int DeactivationTimeoutSeconds = 60;
    private bool _isSystemSleeping;

    // Auth error handling: prevent duplicate concurrent refresh+reconnect cycles
    private readonly SemaphoreSlim _authRefreshLock = new(1, 1);

    private WebSocketRegistry()
    {
        // Subscribe to system sleep/wake events (matches macOS NSWorkspace willSleep/didWake)
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    // --- Sleep/Wake Handling (Section 2.2) ---

    /// <summary>
    /// Handle system sleep/wake events.
    /// On Suspend: disconnect all WebSockets cleanly.
    /// On Resume: wait 2 seconds for network stack, then verify and reconnect.
    /// Matches macOS willSleepNotification / didWakeNotification.
    /// </summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                _isSystemSleeping = true;
                FileLogger.Instance.Info("WebSocketRegistry", "System sleeping — disconnecting all transports");
                DisconnectAllForSleep();
                break;

            case PowerModes.Resume:
                _isSystemSleeping = false;
                FileLogger.Instance.Info("WebSocketRegistry", "System waking — reconnecting after 2s delay");
                _ = ReconnectAllAfterDelay(TimeSpan.FromSeconds(2));
                break;
        }
    }

    private async void DisconnectAllForSleep()
    {
        // Intentional disconnect — don't trigger reconnect logic
        try
        {
            var tasks = new List<Task>();
            if (_unifiedAudioTransport != null) tasks.Add(DisconnectSafe(_unifiedAudioTransport));
            if (_dictationTransport != null) tasks.Add(DisconnectSafe(_dictationTransport));
            if (_multiAgentTransport != null) tasks.Add(DisconnectSafe(_multiAgentTransport));
            if (_voiceToTextTransport != null) tasks.Add(DisconnectSafe(_voiceToTextTransport));

            // Keep meeting transport if meeting is active
            if (MeetingClient != null && MeetingService.Instance.IsActive)
            {
                FileLogger.Instance.Info("WebSocketRegistry", "Keeping meeting transport alive during sleep");
            }
            else if (_meetingTransport != null)
            {
                tasks.Add(DisconnectSafe(_meetingTransport));
            }

            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warning("WebSocketRegistry", $"Sleep disconnect error: {ex.Message}");
        }
    }

    private static async Task DisconnectSafe(WebSocketTransport transport)
    {
        try { await transport.DisconnectAsync(); }
        catch (Exception ex) { FileLogger.Instance.Warning("WebSocketRegistry", $"Disconnect failed: {ex.Message}"); }
    }

    private async Task ReconnectAllAfterDelay(TimeSpan delay)
    {
        await Task.Delay(delay);
        if (_isSystemSleeping) return; // System went back to sleep

        FileLogger.Instance.Info("WebSocketRegistry", "Reconnecting transports after wake...");
        await VerifyAllConnections();
    }

    // --- App Lifecycle (Section 2.3) ---

    /// <summary>
    /// Called when app is activated (window focused). Reconnect if deactivated for >60s.
    /// Matches macOS didBecomeActive observer.
    /// </summary>
    public async Task OnAppActivated()
    {
        _deactivationTimer?.Dispose();
        _deactivationTimer = null;

        if (_deactivatedAt != null)
        {
            var elapsed = DateTime.UtcNow - _deactivatedAt.Value;
            _deactivatedAt = null;

            if (elapsed.TotalSeconds > DeactivationTimeoutSeconds)
            {
                FileLogger.Instance.Info("WebSocketRegistry", $"App reactivated after {elapsed.TotalSeconds:F0}s — verifying connections");
                await VerifyAllConnections();
            }
        }
    }

    /// <summary>
    /// Called when app is deactivated (minimized/hidden). After 60s, disconnect non-essential.
    /// Keeps meeting transport alive if meeting is active.
    /// Matches macOS resignActive observer.
    /// </summary>
    public void OnAppDeactivated()
    {
        _deactivatedAt = DateTime.UtcNow;

        // Set timer to disconnect non-essential transports after 60s
        _deactivationTimer?.Dispose();
        _deactivationTimer = new Timer(_ =>
        {
            FileLogger.Instance.Info("WebSocketRegistry", "App deactivated for >60s — disconnecting non-essential transports");
            // Don't disconnect meeting if active
            _ = DisconnectSafe(_multiAgentTransport!);
        }, null, TimeSpan.FromSeconds(DeactivationTimeoutSeconds), Timeout.InfiniteTimeSpan);
    }

    // --- Pending Transport Pattern (Section 2.4) ---

    /// <summary>
    /// Prepare a pending transport with fresh token. Does NOT disrupt active connections.
    /// Call SwapPendingTransport() when the active operation completes.
    /// Matches macOS pendingUnifiedAudioTransport pattern.
    /// </summary>
    public async Task PreparePendingUnifiedAudioTransport()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("unified_audio_ws_api");
        if (baseUrl == null) return;

        var tenantId = AuthManager.Instance.Storage.TenantId;
        if (string.IsNullOrEmpty(tenantId)) return;

        var sessionId = Guid.NewGuid().ToString("N").Substring(0, 24);
        var url = $"{baseUrl}/{tenantId}/{sessionId}/invoke-predictor";

        FileLogger.Instance.Info("WebSocketRegistry", "Preparing pending unified audio transport...");

        _pendingUnifiedAudioTransport?.Dispose();
        _pendingUnifiedAudioTransport = new WebSocketTransport(url);
        WireAuthErrorHandler(_pendingUnifiedAudioTransport);
        await _pendingUnifiedAudioTransport.ConnectAsync();
    }

    /// <summary>
    /// Swap the pending transport into active use. Dispose the old transport.
    /// Called when ViewCoordinator confirms no active prediction/dictation in progress.
    /// </summary>
    public void SwapPendingTransport()
    {
        if (_pendingUnifiedAudioTransport == null) return;

        // Only swap when no active operation
        var state = ViewCoordinator.Instance.CombinedPredictionState;
        var dictState = ViewCoordinator.Instance.DictationState;
        if (state != CombinedPredictionState.Inactive || dictState != DictationState.Inactive)
        {
            FileLogger.Instance.Debug("WebSocketRegistry", "Active operation in progress — deferring swap");
            return;
        }

        FileLogger.Instance.Info("WebSocketRegistry", "Swapping pending transport to active");
        _oldUnifiedAudioTransport = _unifiedAudioTransport;
        _unifiedAudioTransport = _pendingUnifiedAudioTransport;
        UnifiedAudioClient = new UnifiedAudioSocketClient(_unifiedAudioTransport);
        _pendingUnifiedAudioTransport = null;

        // Dispose old transport
        _oldUnifiedAudioTransport?.Dispose();
        _oldUnifiedAudioTransport = null;
    }

    // --- Auth Error Handling ---

    /// <summary>
    /// When any transport gets a token-expired error, refresh the token and reconnect all affected transports.
    /// Uses a lock so only one refresh cycle runs at a time even if multiple transports fail simultaneously.
    /// </summary>
    private async void OnTransportAuthError(object? sender, EventArgs e)
    {
        if (!await _authRefreshLock.WaitAsync(0))
        {
            FileLogger.Instance.Debug("WebSocketRegistry", "Auth refresh already in progress, skipping duplicate");
            return;
        }

        try
        {
            FileLogger.Instance.Info("WebSocketRegistry", "Token expired on WebSocket — refreshing token...");
            var refreshed = await TokenRefreshService.Instance.EnsureTokenRefreshed();

            if (refreshed)
            {
                FileLogger.Instance.Info("WebSocketRegistry", "Token refreshed — reconnecting transports with fresh token");
                // Reconnect all transports that had auth errors (they'll pick up the new token from AuthManager.Storage)
                var tasks = new List<Task>();
                if (_unifiedAudioTransport != null) tasks.Add(_unifiedAudioTransport.ReconnectAfterTokenRefresh());
                if (_dictationTransport != null) tasks.Add(_dictationTransport.ReconnectAfterTokenRefresh());
                if (_multiAgentTransport != null) tasks.Add(_multiAgentTransport.ReconnectAfterTokenRefresh());
                if (_meetingTransport != null) tasks.Add(_meetingTransport.ReconnectAfterTokenRefresh());
                await Task.WhenAll(tasks);
            }
            else
            {
                FileLogger.Instance.Error("WebSocketRegistry", "Token refresh failed — user may need to re-login");
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("WebSocketRegistry", $"Auth error recovery failed: {ex.Message}");
        }
        finally
        {
            _authRefreshLock.Release();
        }
    }

    private void WireAuthErrorHandler(WebSocketTransport transport)
    {
        transport.AuthErrorDetected += OnTransportAuthError;
    }

    // --- Transport Connect Methods ---

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

        var sessionId = Guid.NewGuid().ToString("N").Substring(0, 24);
        var url = $"{baseUrl}/{tenantId}/{sessionId}/invoke-predictor";

        FileLogger.Instance.Info("WebSocketRegistry", $"Connecting unified audio to: {baseUrl}/.../{sessionId}/invoke-predictor");

        _unifiedAudioTransport?.Dispose();
        _unifiedAudioTransport = new WebSocketTransport(url);
        WireAuthErrorHandler(_unifiedAudioTransport);
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
        WireAuthErrorHandler(_dictationTransport);
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
        WireAuthErrorHandler(_multiAgentTransport);
        MultiAgentClient = new MultiAgentSocketClient(_multiAgentTransport);

        await _multiAgentTransport.ConnectAsync();
        FileLogger.Instance.Info("WebSocketRegistry", "Multi-agent transport connected");
    }

    public async Task ConnectMeetingTransport(string meetingId)
    {
        var token = AuthManager.Instance.Storage.UserToken;
        if (string.IsNullOrEmpty(token))
        {
            FileLogger.Instance.Warning("WebSocketRegistry", "Token not available for meeting WebSocket URL");
            return;
        }

        var meetingWsBase = AppConfiguration.Instance.MeetingWebSocketBaseUrl;
        var encodedToken = Uri.EscapeDataString(token);
        var url = $"{meetingWsBase}/frontend/ws/{meetingId}?token={encodedToken}";

        FileLogger.Instance.Info("WebSocketRegistry", $"Connecting meeting transport for meeting: {meetingId}");

        _meetingTransport?.Dispose();
        _meetingTransport = new WebSocketTransport(url);
        WireAuthErrorHandler(_meetingTransport);
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

    // --- Summary WebSocket (streaming summary generation) ---

    /// <summary>
    /// Connect to summary WebSocket for streaming summary generation.
    /// URL: wss://meetings.us-east-1.ve.ai/genarateSummary/ws/{meetingId}?token=...
    /// Note: "genarateSummary" typo matches the actual backend path.
    /// </summary>
    public async Task ConnectSummaryTransport(string meetingId)
    {
        var token = AuthManager.Instance.Storage.UserToken;
        if (string.IsNullOrEmpty(token))
        {
            FileLogger.Instance.Warning("WebSocketRegistry", "Token not available for summary WebSocket");
            return;
        }

        var encodedToken = Uri.EscapeDataString(token);
        var summaryWsBase = AppConfiguration.Instance.SummaryWebSocketBaseUrl;
        var url = $"{summaryWsBase}/genarateSummary/ws/{meetingId}?token={encodedToken}";

        FileLogger.Instance.Info("WebSocketRegistry", $"Connecting summary transport for meeting: {meetingId}");

        _summaryTransport?.Dispose();
        _summaryTransport = new WebSocketTransport(url, new RetryPolicy { MaxRetries = 3, InitialDelay = 1.0, MaxDelay = 10.0, BackoffMultiplier = 1.5 });
        SummaryClient = new SummarySocketClient(_summaryTransport);

        await _summaryTransport.ConnectAsync();
        FileLogger.Instance.Info("WebSocketRegistry", "Summary transport connected");
    }

    public async Task DisconnectSummaryTransport()
    {
        _summaryTransport?.Dispose();
        _summaryTransport = null;
        SummaryClient = null;
        await Task.CompletedTask;
    }

    public async Task DisconnectAll()
    {
        FileLogger.Instance.Info("WebSocketRegistry", "Disconnecting all transports...");
        _unifiedAudioTransport?.Dispose();
        _dictationTransport?.Dispose();
        _multiAgentTransport?.Dispose();
        _voiceToTextTransport?.Dispose();
        _meetingTransport?.Dispose();
        _summaryTransport?.Dispose();
        _pendingUnifiedAudioTransport?.Dispose();
        _oldUnifiedAudioTransport?.Dispose();

        _unifiedAudioTransport = null;
        _dictationTransport = null;
        _multiAgentTransport = null;
        _voiceToTextTransport = null;
        _meetingTransport = null;
        _summaryTransport = null;
        _pendingUnifiedAudioTransport = null;
        _oldUnifiedAudioTransport = null;

        UnifiedAudioClient = null;
        VoiceToTextClient = null;
        MultiAgentClient = null;
        MeetingClient = null;
        SummaryClient = null;
        await Task.CompletedTask;
    }

    public async Task VerifyAllConnections()
    {
        if (_unifiedAudioTransport != null)
            await _unifiedAudioTransport.VerifyConnectionHealth();
        if (_dictationTransport != null)
            await _dictationTransport.VerifyConnectionHealth();
        if (_multiAgentTransport != null)
            await _multiAgentTransport.VerifyConnectionHealth();
        if (_meetingTransport != null)
            await _meetingTransport.VerifyConnectionHealth();
    }

    public void Dispose()
    {
        _deactivationTimer?.Dispose();
        _authRefreshLock.Dispose();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        DisconnectAll();
    }
}
