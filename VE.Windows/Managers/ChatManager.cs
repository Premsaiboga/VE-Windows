using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Managers;

/// <summary>
/// Chat manager with its OWN WebSocket connection.
/// Does NOT use WebSocketTransport or WebSocketRegistry to avoid any interference.
/// Matches macOS ChatManager exactly: direct WebSocket, same payload, same response parsing.
/// </summary>
public sealed class ChatManager : INotifyPropertyChanged
{
    public static ChatManager Instance { get; } = new();

    private bool _isStreaming;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private ChatMessage? _currentAssistantMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public bool IsStreaming
    {
        get => _isStreaming;
        private set { _isStreaming = value; OnPropertyChanged(); }
    }

    private ChatManager() { }

    public async Task SendMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsStreaming) return;

        var userMessage = new ChatMessage { Role = ChatRole.User, Content = text };
        var assistantMessage = new ChatMessage { Role = ChatRole.Assistant, Content = "", IsStreaming = true };

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Messages.Add(userMessage);
            Messages.Add(assistantMessage);
        });

        _currentAssistantMessage = assistantMessage;
        IsStreaming = true;

        try
        {
            // Get connection info
            var baseUrl = Services.BaseURLService.Instance.GetBaseUrl("chat_ws_api");
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            var token = AuthManager.Instance.Storage.UserToken;

            if (baseUrl == null || string.IsNullOrEmpty(workspaceId) || string.IsNullOrEmpty(token))
            {
                FileLogger.Instance.Error("ChatManager", $"Missing: baseUrl={baseUrl != null}, workspace={!string.IsNullOrEmpty(workspaceId)}, token={!string.IsNullOrEmpty(token)}");
                SetAssistant(assistantMessage, "Not connected. Please check your login.");
                FinishStreaming(assistantMessage);
                return;
            }

            var sessionId = Guid.NewGuid().ToString("N").Substring(0, 24);
            var encodedToken = Uri.EscapeDataString(token);
            var url = $"{baseUrl}/{workspaceId}/{sessionId}/multi_agent_chat_streaming?token={encodedToken}";

            FileLogger.Instance.Info("ChatManager", $"Connecting to: {baseUrl}/.../{sessionId}/multi_agent_chat_streaming");

            // Close previous WebSocket
            CleanupWebSocket();

            // Create NEW WebSocket with headers matching macOS EXACTLY
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            // macOS stores token with "Bearer " prefix; Windows stores raw JWT
            _ws.Options.SetRequestHeader("Authorization", $"Bearer {token}");
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

            // Connect
            await _ws.ConnectAsync(new Uri(url), _cts.Token);

            if (_ws.State != WebSocketState.Open)
            {
                FileLogger.Instance.Error("ChatManager", $"WebSocket not open: {_ws.State}");
                SetAssistant(assistantMessage, "Failed to connect. Please try again.");
                FinishStreaming(assistantMessage);
                return;
            }

            FileLogger.Instance.Info("ChatManager", "WebSocket connected, sending payload...");

            // Start receiving messages BEFORE sending (so we don't miss the response)
            _ = Task.Run(() => ReceiveLoop(assistantMessage));

            // Build payload - matches macOS buildChatPayload exactly
            var timezone = TimeZoneInfo.Local.Id;
            var payload = new Dictionary<string, object?>
            {
                ["types"] = "text",
                ["query"] = text,
                ["timezone"] = timezone,
                ["location"] = new Dictionary<string, string>
                {
                    ["city"] = "Unknown",
                    ["countryRegion"] = "",
                    ["country"] = "Unknown",
                    ["timezone"] = timezone
                },
                ["web_search"] = true,
                ["knowledge_base_search"] = true,
                ["deep_search"] = false,
                ["deep_research"] = false
            };

            var json = JsonConvert.SerializeObject(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);

            FileLogger.Instance.Info("ChatManager", $"Sent payload: {text.Substring(0, Math.Min(50, text.Length))}...");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatManager", $"Send failed: {ex.Message}");
            SetAssistant(assistantMessage, $"Error: {ex.Message}");
            FinishStreaming(assistantMessage);
        }
    }

    private async Task ReceiveLoop(ChatMessage assistantMessage)
    {
        var buffer = new byte[8192];
        try
        {
            while (_ws?.State == WebSocketState.Open && !(_cts?.IsCancellationRequested ?? true))
            {
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, _cts!.Token);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    FileLogger.Instance.Info("ChatManager", "Server closed connection");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(ms.ToArray());
                    HandleMessage(message, assistantMessage);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            FileLogger.Instance.Warning("ChatManager", $"Receive error: {ex.Message}");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatManager", $"Receive loop error: {ex.Message}");
        }

        // If streaming wasn't finished, show error
        if (IsStreaming && string.IsNullOrEmpty(assistantMessage.Content))
        {
            SetAssistant(assistantMessage, "Connection lost. Please try again.");
            FinishStreaming(assistantMessage);
        }
    }

    private void HandleMessage(string message, ChatMessage assistantMessage)
    {
        try
        {
            var json = JObject.Parse(message);
            var keys = string.Join(", ", json.Properties().Select(p => p.Name));
            FileLogger.Instance.Info("ChatManager", $"Keys: [{keys}] Data: {message.Substring(0, Math.Min(200, message.Length))}");

            // Match macOS handleMessage order EXACTLY:
            // 1. error field (string)
            // 2. status == "error"
            // 3. stream_end (bool) - with final answer chunk
            // 4. status_code (presence = error for chat)
            // 5. answer chunk (streaming)

            // Step text (thinking indicator) - can coexist with other fields
            if (json.ContainsKey("step"))
            {
                var step = json["step"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(step))
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        if (string.IsNullOrEmpty(assistantMessage.Content))
                            assistantMessage.ThinkingContent = step;
                    });
                }
            }

            // Citations - can coexist with other fields
            if (json.ContainsKey("citations"))
            {
                try
                {
                    var citationsObj = json["citations"];
                    var citations = new List<Citation>();
                    if (citationsObj is JObject citDict)
                    {
                        foreach (var prop in citDict.Properties())
                        {
                            var cit = prop.Value;
                            citations.Add(new Citation
                            {
                                Title = cit["title"]?.ToString() ?? "",
                                Url = cit["url"]?.ToString() ?? "",
                                Snippet = cit["text"]?.ToString() ?? "",
                            });
                        }
                    }
                    if (citations.Count > 0)
                    {
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            assistantMessage.Citations = citations;
                        });
                    }
                }
                catch { }
            }

            // 1. Error field
            var errorStr = json["error"]?.Type == JTokenType.String ? json["error"]?.ToString() : null;
            if (!string.IsNullOrEmpty(errorStr))
            {
                SetAssistant(assistantMessage, $"Error: {errorStr}");
                FinishStreaming(assistantMessage);
                return;
            }

            // 2. Status == error
            var status = json["status"]?.Type == JTokenType.String ? json["status"]?.ToString() : null;
            if (status?.ToLowerInvariant() == "error")
            {
                var err = json["message"]?.ToString() ?? "Server error";
                SetAssistant(assistantMessage, $"Error: {err}");
                FinishStreaming(assistantMessage);
                return;
            }
            if (status == "cancelled") return;

            // 3. Stream end
            if (json.ContainsKey("stream_end") && (json["stream_end"]?.Value<bool>() ?? false))
            {
                var finalChunk = json["answer"]?.ToString();
                if (!string.IsNullOrEmpty(finalChunk))
                {
                    AppendToAssistant(assistantMessage, finalChunk);
                }
                FinishStreaming(assistantMessage);
                return;
            }

            // 4. Status code (presence = error for chat, per macOS)
            if (json.ContainsKey("status_code"))
            {
                var statusMsg = json["status_message"]?.ToString() ?? "Unknown error";
                FileLogger.Instance.Error("ChatManager", $"status_code present: {statusMsg}");
                SetAssistant(assistantMessage, $"Error: {statusMsg}");
                FinishStreaming(assistantMessage);
                return;
            }

            // 5. Answer chunk (streaming)
            if (json.ContainsKey("answer"))
            {
                var answer = json["answer"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(answer))
                {
                    AppendToAssistant(assistantMessage, answer);
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatManager", $"Parse error: {ex.Message}");
        }
    }

    private void AppendToAssistant(ChatMessage msg, string chunk)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            msg.Content += chunk;
        });
    }

    private void SetAssistant(ChatMessage msg, string content)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (string.IsNullOrEmpty(msg.Content))
                msg.Content = content;
        });
    }

    private void FinishStreaming(ChatMessage msg)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            msg.IsStreaming = false;
            IsStreaming = false;
        });
    }

    private void CleanupWebSocket()
    {
        _cts?.Cancel();
        try { _ws?.Dispose(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _ws = null;
        _cts = null;
    }

    public void ClearChat()
    {
        CleanupWebSocket();
        Messages.Clear();
        _currentAssistantMessage = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
