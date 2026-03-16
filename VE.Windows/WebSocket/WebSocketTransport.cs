using System.IO;
using System.Net.WebSockets;
using System.Text;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Services;

namespace VE.Windows.WebSocket;

/// <summary>
/// Low-level WebSocket transport layer with connection guard, keep-alive pings,
/// and exponential backoff reconnection.
/// Matches macOS WebSocketTransport: connection guard prevents duplicate connects,
/// 120-second ping timer prevents idle timeout, sleep/wake handling for clean reconnection.
/// </summary>
public class WebSocketTransport : IDisposable
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private readonly RetryPolicy _retryPolicy;
    private int _retryAttempt;
    private bool _isDisposed;
    private bool _intentionalDisconnect;

    // Connection guard: prevents duplicate concurrent ConnectAsync() calls (matches macOS ConnectionGuard actor)
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private Task? _activeConnectTask;

    // Auth error handling: stop reconnect loop when token is expired
    private bool _authErrorDetected;
    private static readonly string[] AuthErrorPatterns = new[]
    {
        "Token has expired", "Unauthorized", "jwt expired", "token expired",
        "Invalid token", "Authentication failed"
    };

    // Keep-alive ping timer (matches macOS 120-second interval)
    private Timer? _pingTimer;
    private DateTime _lastMessageTime = DateTime.UtcNow;
    private const int PingIntervalSeconds = 120;

    public string Url { get; private set; }
    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<byte[]>? BinaryReceived;
    public event EventHandler? Connected;
    public event EventHandler<string>? Disconnected;
    public event EventHandler<string>? Error;
    /// <summary>Fired when server returns an auth/token error. Registry should refresh token and reconnect.</summary>
    public event EventHandler? AuthErrorDetected;

    public WebSocketTransport(string url, RetryPolicy? retryPolicy = null)
    {
        Url = url;
        _retryPolicy = retryPolicy ?? RetryPolicy.Default;
    }

    public void UpdateUrl(string newUrl)
    {
        Url = newUrl;
    }

    /// <summary>
    /// Connect with guard: if a connection is already in progress, await it instead of starting a new one.
    /// Matches macOS ConnectionGuard actor pattern.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_isDisposed) return;

        // Fast path: already connected
        if (_webSocket?.State == WebSocketState.Open) return;

        // If a connection is in progress, await it
        var existing = _activeConnectTask;
        if (existing != null)
        {
            FileLogger.Instance.Debug("WebSocket", "Awaiting existing connection attempt...");
            await existing;
            return;
        }

        if (!await _connectLock.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            FileLogger.Instance.Warning("WebSocket", "Connection lock timeout");
            return;
        }

        try
        {
            // Double-check after acquiring lock
            if (_webSocket?.State == WebSocketState.Open) return;
            if (_activeConnectTask != null)
            {
                await _activeConnectTask;
                return;
            }

            _activeConnectTask = ConnectInternalAsync();
            await _activeConnectTask;
        }
        finally
        {
            _activeConnectTask = null;
            try { if (!_isDisposed) _connectLock.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task ConnectInternalAsync()
    {
        _intentionalDisconnect = false;
        _authErrorDetected = false;

        try
        {
            await DisconnectInternalAsync();

            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            // Add auth headers — macOS stores token WITH "Bearer " prefix; Windows stores raw JWT
            var token = AuthManager.Instance.Storage.UserToken;
            if (!string.IsNullOrEmpty(token))
            {
                _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
            }

            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

            FileLogger.Instance.Info("WebSocket", $"Connecting to {Url}...");
            await _webSocket.ConnectAsync(new Uri(Url), _cts.Token);

            _retryAttempt = 0;
            _lastMessageTime = DateTime.UtcNow;
            FileLogger.Instance.Info("WebSocket", $"Connected to {Url}");
            Connected?.Invoke(this, EventArgs.Empty);

            // Start keep-alive ping timer
            StartPingTimer();

            // Start receiving
            _ = Task.Run(() => ReceiveLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("WebSocket", $"Connect failed: {Url} - {ex.Message}");
            Error?.Invoke(this, ex.Message);
            await ScheduleReconnect();
        }
    }

    public async Task DisconnectAsync()
    {
        _intentionalDisconnect = true;
        StopPingTimer();
        await DisconnectInternalAsync();
        Disconnected?.Invoke(this, "Intentional disconnect");
    }

    private async Task DisconnectInternalAsync()
    {
        StopPingTimer();
        _cts?.Cancel();

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token);
                }
            }
            catch { }

            _webSocket.Dispose();
            _webSocket = null;
        }

        _cts?.Dispose();
        _cts = null;
    }

    public async Task SendAsync(string message)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected");
        }

        var bytes = Encoding.UTF8.GetBytes(message);
        await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
    }

    public async Task SendAsync(byte[] data)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected");
        }

        await _webSocket.SendAsync(data, WebSocketMessageType.Binary, true, _cts?.Token ?? CancellationToken.None);
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[8192];

        try
        {
            while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(buffer, ct);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                // Reset ping timer on any message received
                _lastMessageTime = DateTime.UtcNow;

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    FileLogger.Instance.Info("WebSocket", $"Server closed connection: {Url}");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(ms.ToArray());

                    // Check for auth/token errors — stop reconnect loop and signal for token refresh
                    if (IsAuthError(message))
                    {
                        FileLogger.Instance.Warning("WebSocket", $"Auth error detected on {Url} — stopping reconnect, requesting token refresh");
                        _authErrorDetected = true;
                        AuthErrorDetected?.Invoke(this, EventArgs.Empty);
                        break; // Exit receive loop — don't reconnect with stale token
                    }

                    MessageReceived?.Invoke(this, message);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    BinaryReceived?.Invoke(this, ms.ToArray());
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            FileLogger.Instance.Warning("WebSocket", $"Receive error: {Url} - {ex.Message}");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("WebSocket", $"Receive loop error: {ex.Message}");
        }

        if (!_intentionalDisconnect && !_isDisposed)
        {
            Disconnected?.Invoke(this, "Connection lost");
            await ScheduleReconnect();
        }
    }

    private async Task ScheduleReconnect()
    {
        if (_intentionalDisconnect || _isDisposed) return;
        // Don't blindly reconnect with expired token — let AuthErrorDetected handler do the refresh + reconnect
        if (_authErrorDetected) return;
        if (!_retryPolicy.ShouldRetry(_retryAttempt)) return;

        var delay = _retryPolicy.CalculateDelay(_retryAttempt);
        _retryAttempt++;

        FileLogger.Instance.Info("WebSocket", $"Reconnecting in {delay.TotalSeconds:F1}s (attempt {_retryAttempt})...");
        await Task.Delay(delay);

        if (!_intentionalDisconnect && !_isDisposed)
        {
            await ConnectAsync();
        }
    }

    /// <summary>
    /// Called after token has been refreshed. Clears the auth error flag and reconnects with fresh token.
    /// </summary>
    public async Task ReconnectAfterTokenRefresh()
    {
        _authErrorDetected = false;
        _retryAttempt = 0;
        FileLogger.Instance.Info("WebSocket", $"Reconnecting after token refresh: {Url}");
        await ConnectAsync();
    }

    private static bool IsAuthError(string message)
    {
        foreach (var pattern in AuthErrorPatterns)
        {
            if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // --- Keep-alive ping (matches macOS 120-second interval) ---

    /// <summary>
    /// Start periodic ping timer. Sends WebSocket ping every 120 seconds to prevent
    /// server/proxy idle timeout (typically 5 minutes). Timer resets on any message received.
    /// </summary>
    private void StartPingTimer()
    {
        StopPingTimer();
        _pingTimer = new Timer(OnPingTimer, null,
            TimeSpan.FromSeconds(PingIntervalSeconds),
            TimeSpan.FromSeconds(PingIntervalSeconds));
    }

    private void StopPingTimer()
    {
        _pingTimer?.Dispose();
        _pingTimer = null;
    }

    private async void OnPingTimer(object? state)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        var idleTime = DateTime.UtcNow - _lastMessageTime;
        if (idleTime.TotalSeconds < PingIntervalSeconds - 10) return; // Skip if recent activity

        try
        {
            // Send an empty text frame as a ping (compatible with all servers)
            var pingBytes = Encoding.UTF8.GetBytes("ping");
            await _webSocket.SendAsync(pingBytes, WebSocketMessageType.Text, true,
                _cts?.Token ?? CancellationToken.None);
            _lastMessageTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warning("WebSocket", $"Ping failed: {ex.Message}");
            // Connection may be dead — trigger reconnect
            if (!_intentionalDisconnect && !_isDisposed)
            {
                await ScheduleReconnect();
            }
        }
    }

    /// <summary>
    /// Verify connection health. If not connected, attempt reconnect.
    /// Called after system wake to ensure connections are alive.
    /// </summary>
    public async Task VerifyConnectionHealth()
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            FileLogger.Instance.Warning("WebSocket", $"Health check failed: {Url} - not open");
            if (!_intentionalDisconnect)
            {
                _retryAttempt = 0; // Reset backoff after wake
                await ConnectAsync();
            }
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        _intentionalDisconnect = true;
        StopPingTimer();
        _cts?.Cancel();
        _webSocket?.Dispose();
        _cts?.Dispose();
        _connectLock.Dispose();
    }
}
