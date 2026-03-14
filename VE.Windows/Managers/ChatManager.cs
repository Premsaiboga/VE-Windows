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
    private WebSocketTransport? _activeTransport;
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
            // Connect WebSocket (fresh session for each message)
            await WebSocketRegistry.Instance.ConnectMultiAgentTransport();

            // Get the transport directly (bypass MultiAgentSocketClient)
            var transport = WebSocketRegistry.Instance.MultiAgentTransport;

            if (transport == null || !transport.IsConnected)
            {
                // Wait for connection (5s max)
                var maxWait = DateTime.UtcNow.AddSeconds(5);
                while (transport != null && !transport.IsConnected && DateTime.UtcNow < maxWait)
                {
                    await Task.Delay(100);
                }
            }

            if (transport == null || !transport.IsConnected)
            {
                UpdateAssistant(assistantMessage, "Failed to connect to AI service. Please check your connection.");
                FinishStreaming(assistantMessage);
                return;
            }

            // Subscribe directly to transport messages (like macOS ChatManager does)
            _activeTransport = transport;
            transport.MessageReceived -= OnTransportMessage;
            transport.MessageReceived += OnTransportMessage;

            // Build and send the chat payload
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
            FileLogger.Instance.Info("ChatManager", $"Sending chat: {text.Substring(0, Math.Min(50, text.Length))}...");
            await transport.SendAsync(json);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatManager", $"Send failed: {ex.Message}");
            UpdateAssistant(assistantMessage, $"Error: {ex.Message}");
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
                $"Received keys: [{keys}] - {message.Substring(0, Math.Min(300, message.Length))}");

            var assistantMessage = _currentAssistantMessage;
            if (assistantMessage == null) return;

            // 1. Check status field
            var status = json["status"]?.Type == JTokenType.String ? json["status"]?.ToString() : null;
            if (status == "cancelled") return;
            if (status == "error")
            {
                var error = json["error"]?.ToString() ?? json["message"]?.ToString() ?? "Server error";
                UpdateAssistant(assistantMessage, $"Error: {error}");
                FinishStreaming(assistantMessage);
                return;
            }

            // 2. Check error field (only non-empty strings)
            var errorStr = json["error"]?.Type == JTokenType.String ? json["error"]?.ToString() : null;
            if (!string.IsNullOrEmpty(errorStr))
            {
                UpdateAssistant(assistantMessage, $"Error: {errorStr}");
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
        });
    }

    private void UpdateAssistant(ChatMessage msg, string content)
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
            CleanupTransport();
        });
    }

    private void CleanupTransport()
    {
        if (_activeTransport != null)
        {
            _activeTransport.MessageReceived -= OnTransportMessage;
            _activeTransport = null;
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
