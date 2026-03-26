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
            // Log all keys for debugging
            var keys = string.Join(", ", json.Properties().Select(p => p.Name));
            FileLogger.Instance.Info("MultiAgentClient",
                $"Received keys: [{keys}] - {message.Substring(0, Math.Min(200, message.Length))}");

            // Matches macOS ChatManager.handleMessage order exactly:
            // 1. status == "error" → error
            // 2. error field (non-empty string) → error
            // 3. answer chunk → accumulate + stream
            // 4. stream_end → complete
            // NOTE: macOS ChatManager does NOT check status_code for chat messages!

            // 1. Check status field
            var status = json["status"]?.Type == JTokenType.String ? json["status"]?.ToString() : null;
            if (status == "cancelled")
            {
                FileLogger.Instance.Info("MultiAgentClient", "Received cancelled status, ignoring");
                return;
            }
            if (status == "error")
            {
                var error = json["error"]?.ToString() ?? json["message"]?.ToString() ?? "Server error";
                FileLogger.Instance.Error("MultiAgentClient", $"Status error: {error}");
                OnError?.Invoke(this, error);
                return;
            }

            // 2. Check error field - only non-empty strings (macOS: if let error = json["error"] as? String)
            var errorStr = json["error"]?.Type == JTokenType.String ? json["error"]?.ToString() : null;
            if (!string.IsNullOrEmpty(errorStr))
            {
                FileLogger.Instance.Error("MultiAgentClient", $"Error field: {errorStr}");
                OnError?.Invoke(this, errorStr);
                return;
            }

            // 3. Step text (thinking indicator)
            if (json.ContainsKey("step"))
            {
                var step = json["step"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(step))
                {
                    OnStepReceived?.Invoke(this, step);
                }
            }

            // 4. Citations (can arrive with any message)
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
                catch (Exception ex)
                {
                    FileLogger.Instance.Warning("MultiAgentSocket", $"Citation parse failed: {ex.Message}");
                }
            }

            // 5. Stream end - check BEFORE answer (matches macOS: processes final chunk in stream_end)
            if (json.ContainsKey("stream_end") && (json["stream_end"]?.Value<bool>() ?? false))
            {
                // Process any final answer chunk included with stream_end
                var finalChunk = json["answer"]?.ToString();
                if (!string.IsNullOrEmpty(finalChunk))
                {
                    OnResponseChunk?.Invoke(this, finalChunk);
                }

                OnResponseComplete?.Invoke(this, new ChatResponse { IsComplete = true });
                return;
            }

            // 6. Answer chunk (streaming response)
            if (json.ContainsKey("answer"))
            {
                var answer = json["answer"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(answer))
                {
                    OnResponseChunk?.Invoke(this, answer);
                }
                return;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MultiAgentClient",
                $"Parse error: {ex.Message} - Raw: {message.Substring(0, Math.Min(100, message.Length))}");
            OnError?.Invoke(this, $"Failed to parse response: {ex.Message}");
        }
    }
}
