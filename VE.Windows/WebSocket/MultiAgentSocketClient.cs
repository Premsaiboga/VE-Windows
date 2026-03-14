using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.WebSocket;

/// <summary>
/// Chat/instruction WebSocket client for AI conversations.
/// Matches macOS MultiAgentSocketClient message format exactly.
///
/// SEND: { types, query, timezone, location, web_search, knowledge_base_search, deep_search, deep_research }
/// RECEIVE: { answer, stream_end, step, citations, error, status_code }
/// </summary>
public class MultiAgentSocketClient
{
    private readonly WebSocketTransport _transport;

    public event EventHandler<string>? OnResponseChunk;
    public event EventHandler<ChatResponse>? OnResponseComplete;
    public event EventHandler<List<Citation>>? OnCitationsReceived;
    public event EventHandler<string>? OnStepReceived;
    public event EventHandler<string>? OnError;

    public bool IsConnected => _transport.IsConnected;

    public MultiAgentSocketClient(WebSocketTransport transport)
    {
        _transport = transport;
        _transport.MessageReceived += HandleMessage;
    }

    public async Task SendChatMessage(string text)
    {
        try
        {
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
            FileLogger.Instance.Info("MultiAgentClient", $"Sending chat: {text.Substring(0, Math.Min(50, text.Length))}...");
            await _transport.SendAsync(json);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MultiAgentClient", $"Send failed: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
        }
    }

    public async Task SendStopAction()
    {
        try
        {
            var payload = JsonConvert.SerializeObject(new { action = "stop" });
            await _transport.SendAsync(payload);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MultiAgentClient", $"Stop action send failed: {ex.Message}");
        }
    }

    private void HandleMessage(object? sender, string message)
    {
        try
        {
            var json = JObject.Parse(message);
            FileLogger.Instance.Info("MultiAgentClient",
                $"Received: {message.Substring(0, Math.Min(200, message.Length))}");

            // Check for error
            if (json.ContainsKey("error"))
            {
                var error = json["error"]?.ToString() ?? "Unknown error";
                FileLogger.Instance.Error("MultiAgentClient", $"Error: {error}");
                OnError?.Invoke(this, error);
                return;
            }

            if (json.ContainsKey("status") && json["status"]?.ToString() == "error")
            {
                var error = json["message"]?.ToString() ?? "Server error";
                OnError?.Invoke(this, error);
                return;
            }

            if (json.ContainsKey("status_code"))
            {
                var statusCode = json["status_code"]?.Value<int>() ?? 0;
                if (statusCode >= 400)
                {
                    var error = json["status_message"]?.ToString() ?? json["error"]?.ToString() ?? "Server error";
                    OnError?.Invoke(this, error);
                    return;
                }
            }

            // Step text (thinking indicator)
            if (json.ContainsKey("step"))
            {
                var step = json["step"]?.ToString() ?? "";
                OnStepReceived?.Invoke(this, step);
            }

            // Answer chunk (streaming response)
            if (json.ContainsKey("answer"))
            {
                var answer = json["answer"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(answer))
                {
                    OnResponseChunk?.Invoke(this, answer);
                }
            }

            // Citations
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
                        OnCitationsReceived?.Invoke(this, citations);
                    }
                }
                catch { }
            }

            // Stream end - response complete
            if (json.ContainsKey("stream_end"))
            {
                var streamEnd = json["stream_end"]?.Value<bool>() ?? false;
                if (streamEnd)
                {
                    var chatResponse = new ChatResponse
                    {
                        IsComplete = true,
                    };
                    OnResponseComplete?.Invoke(this, chatResponse);
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MultiAgentClient",
                $"Parse error: {ex.Message} - Raw: {message.Substring(0, Math.Min(100, message.Length))}");
        }
    }
}
