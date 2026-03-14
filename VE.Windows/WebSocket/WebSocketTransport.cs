using System.IO;
using System.Net.WebSockets;
using System.Text;
using VE.Windows.Helpers;
using VE.Windows.Managers;

namespace VE.Windows.WebSocket;

/// <summary>
/// Low-level WebSocket transport layer.
/// Owns: connect, disconnect, reconnect, retry with backoff, auth headers.
/// Equivalent to macOS WebSocketTransport.
/// </summary>
public class WebSocketTransport : IDisposable
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private readonly RetryPolicy _retryPolicy;
    private int _retryAttempt;
    private bool _isDisposed;
    private bool _intentionalDisconnect;

    public string Url { get; private set; }
    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<byte[]>? BinaryReceived;
    public event EventHandler? Connected;
    public event EventHandler<string>? Disconnected;
    public event EventHandler<string>? Error;

    public WebSocketTransport(string url, RetryPolicy? retryPolicy = null)
    {
        Url = url;
        _retryPolicy = retryPolicy ?? RetryPolicy.Default;
    }

    public void UpdateUrl(string newUrl)
    {
        Url = newUrl;
    }

    public async Task ConnectAsync()
    {
        if (_isDisposed) return;
        _intentionalDisconnect = false;

        try
        {
            await DisconnectInternalAsync();

            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            // Add auth headers - match macOS exactly
            // macOS only sends Authorization: Bearer header on WebSocket connections
            // It does NOT send x-csrf-token or x-workspace-id on WebSockets
            var token = AuthManager.Instance.Storage.UserToken;
            if (!string.IsNullOrEmpty(token))
            {
                _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
            }

            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

            FileLogger.Instance.Info("WebSocket", $"Connecting to {Url}...");
            await _webSocket.ConnectAsync(new Uri(Url), _cts.Token);

            _retryAttempt = 0;
            FileLogger.Instance.Info("WebSocket", $"Connected to {Url}");
            Connected?.Invoke(this, EventArgs.Empty);

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
        await DisconnectInternalAsync();
        Disconnected?.Invoke(this, "Intentional disconnect");
    }

    private async Task DisconnectInternalAsync()
    {
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

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    FileLogger.Instance.Info("WebSocket", $"Server closed connection: {Url}");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(ms.ToArray());
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

    public async Task VerifyConnectionHealth()
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            FileLogger.Instance.Warning("WebSocket", $"Health check failed: {Url} - not open");
            if (!_intentionalDisconnect)
            {
                await ConnectAsync();
            }
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        _intentionalDisconnect = true;
        _cts?.Cancel();
        _webSocket?.Dispose();
        _cts?.Dispose();
    }
}
