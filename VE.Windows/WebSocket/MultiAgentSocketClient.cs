using Newtonsoft.Json;
using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.WebSocket;

/// <summary>
/// Chat/instruction WebSocket client for AI conversations.
/// Equivalent to macOS MultiAgentSocketClient.
/// </summary>
public class MultiAgentSocketClient
{
    private readonly WebSocketTransport _transport;

    public event EventHandler<string>? OnResponseChunk;
    public event EventHandler<ChatResponse>? OnResponseComplete;
    public event EventHandler<List<Citation>>? OnCitationsReceived;
    public event EventHandler<string>? OnError;

    public bool IsConnected => _transport.IsConnected;

    public MultiAgentSocketClient(WebSocketTransport transport)
    {
        _transport = transport;
        _transport.MessageReceived += HandleMessage;
    }

    public async Task SendChatMessage(string text, string? conversationId = null, string? instructionId = null)
    {
        try
        {
            var payload = new
            {
                type = "chat",
                message = text,
                conversationId,
                instructionId,
                platform = "windows"
            };
            await _transport.SendAsync(JsonConvert.SerializeObject(payload));
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MultiAgentClient", $"Send failed: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
        }
    }

    public async Task SendInstruction(string text)
    {
        try
        {
            var payload = new
            {
                type = "instruction",
                message = text,
                platform = "windows"
            };
            await _transport.SendAsync(JsonConvert.SerializeObject(payload));
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MultiAgentClient", $"Instruction send failed: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
        }
    }

    private void HandleMessage(object? sender, string message)
    {
        try
        {
            var response = JsonConvert.DeserializeObject<AgentResponse>(message);
            if (response == null) return;

            switch (response.Type)
            {
                case "chunk":
                    OnResponseChunk?.Invoke(this, response.Text ?? "");
                    break;
                case "complete":
                    var chatResponse = new ChatResponse
                    {
                        Id = response.Id,
                        Text = response.Text,
                        IsComplete = true,
                        Citations = response.Citations
                    };
                    OnResponseComplete?.Invoke(this, chatResponse);
                    break;
                case "citations":
                    if (response.Citations != null)
                    {
                        OnCitationsReceived?.Invoke(this, response.Citations);
                    }
                    break;
                case "thinking":
                    // Thinking indicator - can be surfaced in UI
                    break;
                case "error":
                    OnError?.Invoke(this, response.Error ?? "Unknown error");
                    break;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MultiAgentClient", $"Parse error: {ex.Message}");
        }
    }

    private class AgentResponse
    {
        [JsonProperty("type")] public string? Type { get; set; }
        [JsonProperty("text")] public string? Text { get; set; }
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("error")] public string? Error { get; set; }
        [JsonProperty("citations")] public List<Citation>? Citations { get; set; }
    }
}
