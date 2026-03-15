using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.WebSocket;

namespace VE.Windows.Managers;

public sealed class ChatManager : INotifyPropertyChanged
{
    public static ChatManager Instance { get; } = new();

    private bool _isStreaming;
    private WebSocketTransport? _chatTransport; // Own transport - no interference
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

        var userMessage = new ChatMessage
        {
            Role = ChatRole.User,
            Content = text
        };

        var assistantMessage = new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "",
            IsStreaming = true
        };

        // Add messages on UI thread
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Messages.Add(userMessage);
            Messages.Add(assistantMessage);
        });

        _currentAssistantMessage = assistantMessage;
        IsStreaming = true;

        try
        {
            // Create OWN WebSocket transport directly (not through WebSocketRegistry)
            // This avoids interference from MultiAgentSocketClient which also subscribes
            // to the same transport's MessageReceived event
            CleanupTransport();

            var baseUrl = Services.BaseURLService.Instance.GetBaseUrl("chat_ws_api");
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            var token = AuthManager.Instance.Storage.UserToken;

            if (baseUrl == null || string.IsNullOrEmpty(workspaceId) || string.IsNullOrEmpty(token))
            {
                FileLogger.Instance.Error("ChatManager", $"Missing connection info: baseUrl={baseUrl != null}, workspaceId={!string.IsNullOrEmpty(workspaceId)}, token={!string.IsNullOrEmpty(token)}");
                SetAssistant(assistantMessage, "Failed to connect. Please check your login.");
                FinishStreaming(assistantMessage);
                return;
            }

            var sessionId = Guid.NewGuid().ToString("N").Substring(0, 24);
            var encodedToken = Uri.EscapeDataString(token);
            var url = $"{baseUrl}/{workspaceId}/{sessionId}/multi_agent_chat_streaming?token={encodedToken}";

            FileLogger.Instance.Info("ChatManager", $"Creating own transport: {baseUrl}/.../{sessionId}");

            _chatTransport = new WebSocketTransport(url);
            _chatTransport.MessageReceived += OnTransportMessage;
            _chatTransport.Error += (s, err) =>
            {
                FileLogger.Instance.Error("ChatManager", $"Transport error: {err}");
                SetAssistant(assistantMessage, $"Connection error: {err}");
                FinishStreaming(assistantMessage);
            };
            _chatTransport.Disconnected += (s, reason) =>
            {
                FileLogger.Instance.Warning("ChatManager", $"Transport disconnected: {reason}");
                if (IsStreaming && string.IsNullOrEmpty(assistantMessage.Content))
                {
                    SetAssistant(assistantMessage, "Connection lost. Please try again.");
                    FinishStreaming(assistantMessage);
                }
            };

            await _chatTransport.ConnectAsync();

            if (!_chatTransport.IsConnected)
            {
                FileLogger.Instance.Error("ChatManager", "Transport failed to connect");
                SetAssistant(assistantMessage, "Failed to connect to AI service. Please try again.");
                FinishStreaming(assistantMessage);
                return;
            }

            FileLogger.Instance.Info("ChatManager", "Transport connected, sending payload");

            // Build and send the chat payload (matches macOS exactly)
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

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            FileLogger.Instance.Info("ChatManager", $"Sending chat payload ({json.Length} chars): {text.Substring(0, Math.Min(50, text.Length))}...");
            await _chatTransport.SendAsync(json);
            FileLogger.Instance.Info("ChatManager", "Payload sent successfully");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatManager", $"Send failed: {ex.Message}");
            SetAssistant(assistantMessage, $"Error: {ex.Message}");
            FinishStreaming(assistantMessage);
        }
    }

    private void OnTransportMessage(object? sender, string message)
    {
        try
        {
            var json = JObject.Parse(message);
            var keys = string.Join(", ", json.Properties().Select(p => p.Name));
            FileLogger.Instance.Info("ChatManager",
                $"MSG keys: [{keys}] - {message.Substring(0, Math.Min(300, message.Length))}");

            var assistantMessage = _currentAssistantMessage;
            if (assistantMessage == null)
            {
                FileLogger.Instance.Warning("ChatManager", "No current assistant message for incoming data");
                return;
            }

            // 1. Check status field
            var status = json["status"]?.Type == JTokenType.String ? json["status"]?.ToString() : null;
            if (status == "cancelled") return;
            if (status == "error")
            {
                var error = json["error"]?.ToString() ?? json["message"]?.ToString() ?? "Server error";
                SetAssistant(assistantMessage, $"Error: {error}");
                FinishStreaming(assistantMessage);
                return;
            }

            // 2. Check error field (only non-empty strings)
            var errorStr = json["error"]?.Type == JTokenType.String ? json["error"]?.ToString() : null;
            if (!string.IsNullOrEmpty(errorStr))
            {
                SetAssistant(assistantMessage, $"Error: {errorStr}");
                FinishStreaming(assistantMessage);
                return;
            }

            // 3. Step text (thinking indicator)
            if (json.ContainsKey("step"))
            {
                var step = json["step"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(step))
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        if (string.IsNullOrEmpty(assistantMessage.Content))
                        {
                            assistantMessage.ThinkingContent = step;
                        }
                    });
                }
            }

            // 4. Citations
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

            // 5. Stream end (check before answer - process final chunk)
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

            // 6. Answer chunk
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
            FileLogger.Instance.Error("ChatManager",
                $"Parse error: {ex.Message} - Raw: {message.Substring(0, Math.Min(100, message.Length))}");
        }
    }

    private void AppendToAssistant(ChatMessage msg, string chunk)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            msg.Content += chunk;
            FileLogger.Instance.Debug("ChatManager", $"Content now: {msg.Content.Length} chars");
        });
    }

    private void SetAssistant(ChatMessage msg, string content)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            msg.Content = content;
        });
    }

    private void FinishStreaming(ChatMessage msg)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            msg.IsStreaming = false;
            IsStreaming = false;
            FileLogger.Instance.Info("ChatManager", $"Streaming finished. Content: {msg.Content.Length} chars");
        });
    }

    private void CleanupTransport()
    {
        if (_chatTransport != null)
        {
            _chatTransport.MessageReceived -= OnTransportMessage;
            _chatTransport.Dispose();
            _chatTransport = null;
        }
    }

    public void ClearChat()
    {
        CleanupTransport();
        Messages.Clear();
        _currentAssistantMessage = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
