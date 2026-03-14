using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;

namespace VE.Windows.WebSocket;

/// <summary>
/// Prediction WebSocket client - sends audio chunks + end payload, receives streaming predictions.
/// Matches macOS UnifiedAudioSocketClient message format exactly.
///
/// SEND: binary audio chunks (PCM), then JSON end payload with metadata + screenshot
/// RECEIVE: { suggested_text, step, output_completed, stream_end, status_code, id, error }
/// </summary>
public class UnifiedAudioSocketClient
{
    private readonly WebSocketTransport _transport;
    private string _accumulatedText = "";
    private DateTime _predictionStartTime;

    public event EventHandler<string>? OnPredictionStreaming;
    public event EventHandler<PredictionResult>? OnPredictionComplete;
    public event EventHandler<string>? OnDictationResult;
    public event EventHandler<string>? OnError;

    public bool IsConnected => _transport.IsConnected;

    public UnifiedAudioSocketClient(WebSocketTransport transport)
    {
        _transport = transport;
        _transport.MessageReceived += HandleMessage;
    }

    public void ResetAccumulatedText()
    {
        _accumulatedText = "";
        _predictionStartTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Send binary audio chunk (PCM 16kHz mono 16-bit).
    /// </summary>
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

    /// <summary>
    /// Send end payload with metadata and screenshot when key is released.
    /// Matches macOS format: { action, audio_completed, audio_format, start_time, end_time,
    ///   timezone, platform, windowTitle, image_data: [base64] }
    /// </summary>
    public async Task SendEndPayload(bool audioCompleted, byte[]? screenshot,
        string? appName = null, string? windowTitle = null)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var startMs = new DateTimeOffset(_predictionStartTime).ToUnixTimeMilliseconds();

            var timezone = TimeZoneInfo.Local.Id;

            var payload = new Dictionary<string, object?>
            {
                ["action"] = "done",
                ["audio_completed"] = audioCompleted,
                ["audio_format"] = "pcm",
                ["start_time"] = startMs,
                ["end_time"] = now,
                ["timezone"] = timezone,
                ["platform"] = $"{appName ?? ScreenCaptureManager.Instance.GetActiveAppName()} - {windowTitle ?? ScreenCaptureManager.Instance.GetActiveWindowTitle()}",
                ["windowTitle"] = windowTitle ?? ScreenCaptureManager.Instance.GetActiveWindowTitle(),
                // Match macOS: include location object (required by backend)
                ["location"] = new Dictionary<string, string>
                {
                    ["city"] = "Unknown",
                    ["countryRegion"] = "",
                    ["country"] = "Unknown",
                    ["timezone"] = timezone
                },
            };

            if (screenshot != null)
            {
                payload["image_data"] = new[] { Convert.ToBase64String(screenshot) };
            }

            var json = JsonConvert.SerializeObject(payload);
            FileLogger.Instance.Info("UnifiedAudioClient", $"Sending end payload ({json.Length} chars)");
            await _transport.SendAsync(json);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("UnifiedAudioClient", $"End payload send failed: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
        }
    }

    /// <summary>
    /// Send stop action (when user presses ESC during prediction).
    /// </summary>
    public async Task SendStopAction()
    {
        try
        {
            var payload = JsonConvert.SerializeObject(new { action = "stop" });
            await _transport.SendAsync(payload);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("UnifiedAudioClient", $"Stop action send failed: {ex.Message}");
        }
    }

    private void HandleMessage(object? sender, string message)
    {
        try
        {
            var json = JObject.Parse(message);
            FileLogger.Instance.Info("UnifiedAudioClient", $"Received: {message.Substring(0, Math.Min(200, message.Length))}");

            // Check for cancelled status (matches macOS: gracefully ignore)
            var status = json["status"]?.ToString();
            if (status == "cancelled")
            {
                FileLogger.Instance.Info("UnifiedAudioClient", "Received cancelled status, ignoring");
                return;
            }

            // Check for error status (matches macOS)
            if (status == "error")
            {
                var error = json["error"]?.ToString() ?? "Error please try again";
                FileLogger.Instance.Error("UnifiedAudioClient", $"Error status: {error}");
                OnError?.Invoke(this, error);
                return;
            }

            // Check for dictation response - enhanced_text field
            // macOS checks: json["enhanced_text"], json["response"]["enhanced_transcription"]["enhanced_text"],
            //               json["enhanced_transcription"]["enhanced_text"]
            var enhancedText = json["enhanced_text"]?.ToString()
                ?? json["response"]?["enhanced_transcription"]?["enhanced_text"]?.ToString()
                ?? json["enhanced_transcription"]?["enhanced_text"]?.ToString();
            if (!string.IsNullOrEmpty(enhancedText))
            {
                OnDictationResult?.Invoke(this, enhancedText);
                return;
            }

            // Check for dictation error (status_code present but no suggested_text/step = dictation context)
            // macOS: if status_code exists and no suggested_text/step fields, treat as dictation response
            if (json.ContainsKey("status_code") && !json.ContainsKey("suggested_text") && !json.ContainsKey("step"))
            {
                var statusCode = json["status_code"]?.Value<int>() ?? 0;
                if (statusCode == 404)
                {
                    // macOS shows "No words detected speak closer" for 404
                    OnError?.Invoke(this, "No words detected. Please speak closer to the microphone.");
                    return;
                }
                else if (statusCode >= 400)
                {
                    var error = json["error"]?.ToString() ?? json["status_message"]?.ToString() ?? "Error please try again";
                    OnError?.Invoke(this, error);
                    return;
                }
            }

            // Accumulate suggested_text
            if (json.ContainsKey("suggested_text"))
            {
                var suggestedText = json["suggested_text"]?.ToString() ?? "";
                _accumulatedText += suggestedText;
            }

            // Show step text (streaming display)
            if (json.ContainsKey("step"))
            {
                var step = json["step"]?.ToString() ?? "";
                OnPredictionStreaming?.Invoke(this, step);
            }
            else if (json.ContainsKey("suggested_text"))
            {
                // If no step field, show accumulated text
                OnPredictionStreaming?.Invoke(this, _accumulatedText);
            }

            // Output completed - prediction is ready for paste
            if (json.ContainsKey("output_completed"))
            {
                var outputCompleted = json["output_completed"]?.Value<bool>() ?? false;
                if (outputCompleted)
                {
                    if (!string.IsNullOrEmpty(_accumulatedText))
                    {
                        FileLogger.Instance.Info("UnifiedAudioClient", $"Prediction complete: {_accumulatedText.Length} chars");
                        OnPredictionComplete?.Invoke(this, new PredictionResult
                        {
                            Id = json["id"]?.ToString() ?? "",
                            Text = _accumulatedText,
                            Status = 200
                        });
                    }
                    else
                    {
                        FileLogger.Instance.Warning("UnifiedAudioClient", "output_completed but no text accumulated");
                        OnError?.Invoke(this, "No words detected. Please speak closer to the microphone.");
                    }
                }
            }

            // Stream end - final signal with ID
            if (json.ContainsKey("stream_end"))
            {
                var streamEnd = json["stream_end"]?.Value<bool>() ?? false;
                if (streamEnd)
                {
                    var id = json["id"]?.ToString() ?? "";
                    var statusCode = json["status_code"]?.Value<int>() ?? 200;

                    // macOS: only handle status_code 500 explicitly for predictions
                    if (statusCode == 500)
                    {
                        var error = json["error"]?.ToString() ?? "Couldn't capture your intent";
                        OnError?.Invoke(this, error);
                    }
                    else if (statusCode == 200 && !string.IsNullOrEmpty(_accumulatedText))
                    {
                        OnPredictionComplete?.Invoke(this, new PredictionResult
                        {
                            Id = id,
                            Text = _accumulatedText,
                            Status = statusCode
                        });
                    }
                    else if (string.IsNullOrEmpty(_accumulatedText))
                    {
                        var errorMsg = json["status_message"]?.ToString() ?? "No words detected. Please speak closer to the microphone.";
                        FileLogger.Instance.Warning("UnifiedAudioClient", $"stream_end with no accumulated text, status: {statusCode}");
                        OnError?.Invoke(this, errorMsg);
                    }

                    // Send response metadata
                    _ = SendResponseMetadata(id);
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("UnifiedAudioClient", $"Parse error: {ex.Message} - Raw: {message.Substring(0, Math.Min(100, message.Length))}");
        }
    }

    private async Task SendResponseMetadata(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        try
        {
            var duration = (DateTime.UtcNow - _predictionStartTime).TotalSeconds;
            var payload = JsonConvert.SerializeObject(new
            {
                id,
                responseMetadata = new { roundTripTime = duration }
            });
            await _transport.SendAsync(payload);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("UnifiedAudioClient", $"Response metadata send failed: {ex.Message}");
        }
    }
}

public class PredictionResult
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public int Status { get; set; }
}
