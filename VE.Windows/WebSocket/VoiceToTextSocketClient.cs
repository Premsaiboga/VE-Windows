using Newtonsoft.Json;
using VE.Windows.Helpers;

namespace VE.Windows.WebSocket;

/// <summary>
/// Voice-to-text WebSocket client for dictation.
/// Equivalent to macOS VoiceToTextSocketClient.
/// </summary>
public class VoiceToTextSocketClient
{
    private readonly WebSocketTransport _transport;

    public event EventHandler<string>? OnTranscriptionReceived;
    public event EventHandler<string>? OnTranscriptionComplete;
    public event EventHandler<string>? OnError;

    public bool IsConnected => _transport.IsConnected;

    public VoiceToTextSocketClient(WebSocketTransport transport)
    {
        _transport = transport;
        _transport.MessageReceived += HandleMessage;
    }

    public async Task SendAudioData(byte[] audioData)
    {
        try
        {
            await _transport.SendAsync(audioData);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("VoiceToTextClient", $"Send failed: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
        }
    }

    public async Task SendEndSignal()
    {
        try
        {
            var payload = JsonConvert.SerializeObject(new { type = "end" });
            await _transport.SendAsync(payload);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("VoiceToTextClient", $"End signal failed: {ex.Message}");
        }
    }

    private void HandleMessage(object? sender, string message)
    {
        try
        {
            var response = JsonConvert.DeserializeObject<TranscriptionResponse>(message);
            if (response == null) return;

            switch (response.Type)
            {
                case "transcription_partial":
                    OnTranscriptionReceived?.Invoke(this, response.Text ?? "");
                    break;
                case "transcription_final":
                    OnTranscriptionComplete?.Invoke(this, response.Text ?? "");
                    break;
                case "error":
                    OnError?.Invoke(this, response.Error ?? "Unknown error");
                    break;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("VoiceToTextClient", $"Parse error: {ex.Message}");
        }
    }

    private class TranscriptionResponse
    {
        [JsonProperty("type")] public string? Type { get; set; }
        [JsonProperty("text")] public string? Text { get; set; }
        [JsonProperty("error")] public string? Error { get; set; }
        [JsonProperty("confidence")] public double? Confidence { get; set; }
    }
}
