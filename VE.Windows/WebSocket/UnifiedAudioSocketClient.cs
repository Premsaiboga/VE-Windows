using Newtonsoft.Json;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Models;

namespace VE.Windows.WebSocket;

/// <summary>
/// Prediction WebSocket client - sends audio + screenshot, receives predictions.
/// Equivalent to macOS UnifiedAudioSocketClient.
/// </summary>
public class UnifiedAudioSocketClient
{
    private readonly WebSocketTransport _transport;

    public event EventHandler<string>? OnPredictionReceived;
    public event EventHandler<PredictionResult>? OnPredictionComplete;
    public event EventHandler<string>? OnError;

    public bool IsConnected => _transport.IsConnected;

    public UnifiedAudioSocketClient(WebSocketTransport transport)
    {
        _transport = transport;
        _transport.MessageReceived += HandleMessage;
    }

    public async Task SendPredictionRequest(byte[] audioData, byte[]? screenshot, string? appContext)
    {
        try
        {
            var payload = new
            {
                type = "prediction",
                audio = Convert.ToBase64String(audioData),
                screenshot = screenshot != null ? Convert.ToBase64String(screenshot) : null,
                context = new
                {
                    activeApp = appContext ?? ScreenCaptureManager.Instance.GetActiveAppName(),
                    activeWindow = ScreenCaptureManager.Instance.GetActiveWindowTitle(),
                    platform = "windows",
                    clipboard = ClipboardManager.Instance.ReadClipboard()
                }
            };

            await _transport.SendAsync(JsonConvert.SerializeObject(payload));
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("UnifiedAudioClient", $"Send failed: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
        }
    }

    public async Task SendAudioChunk(byte[] audioData)
    {
        try
        {
            await _transport.SendAsync(audioData);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("UnifiedAudioClient", $"Audio chunk send failed: {ex.Message}");
        }
    }

    private void HandleMessage(object? sender, string message)
    {
        try
        {
            var response = JsonConvert.DeserializeObject<PredictionResponse>(message);
            if (response == null) return;

            switch (response.Type)
            {
                case "prediction_chunk":
                    OnPredictionReceived?.Invoke(this, response.Text ?? "");
                    break;
                case "prediction_complete":
                    OnPredictionComplete?.Invoke(this, new PredictionResult
                    {
                        Id = response.Id ?? "",
                        Text = response.Text ?? "",
                        Status = response.Status ?? 200
                    });
                    break;
                case "error":
                    OnError?.Invoke(this, response.Error ?? "Unknown error");
                    break;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("UnifiedAudioClient", $"Parse error: {ex.Message}");
        }
    }

    private class PredictionResponse
    {
        [JsonProperty("type")] public string? Type { get; set; }
        [JsonProperty("text")] public string? Text { get; set; }
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("status")] public int? Status { get; set; }
        [JsonProperty("error")] public string? Error { get; set; }
    }
}

public class PredictionResult
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public int Status { get; set; }
}
