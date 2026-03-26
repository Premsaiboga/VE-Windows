using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;

namespace VE.Windows.WebSocket;

/// <summary>
/// Meeting WebSocket client - sends audio chunks, receives transcriptions.
/// Matches macOS MeetingService WebSocket message format.
///
/// SEND: connect payload, audio_data payloads, trigger.transcript.isFinal
/// RECEIVE: transcription/partial_transcription messages, connect event
/// </summary>
public class MeetingSocketClient
{
    private readonly WebSocketTransport _transport;
    private bool _isConnectionConfirmed;
    private readonly List<(byte[] data, string source)> _pendingAudioBuffer = new();

    public event EventHandler<MeetingTranscription>? OnTranscription;
    public event EventHandler<MeetingTranscription>? OnPartialTranscription;
    public event EventHandler? OnConnectionConfirmed;
    public event EventHandler<string>? OnError;

    public bool IsConnected => _transport.IsConnected;

    public MeetingSocketClient(WebSocketTransport transport)
    {
        _transport = transport;
        _transport.MessageReceived += HandleMessage;
    }

    /// <summary>
    /// Send the initial connect payload after WebSocket connects.
    /// </summary>
    public async Task SendConnectPayload(string sessionId, string? title = null)
    {
        try
        {
            var timezone = TimeZoneInfo.Local.Id;
            var tenantId = AuthManager.Instance.Storage.TenantId ?? "";
            var token = AuthManager.Instance.Storage.UserToken ?? "";

            var payload = new Dictionary<string, object?>
            {
                ["source"] = "mic",
                ["type"] = "meetings",
                ["session_id"] = sessionId,
                ["timezone"] = timezone,
                ["token"] = token,
                ["tenant_id"] = tenantId,
                ["is_ai_intelligence_enabled"] = false,
                ["location"] = new Dictionary<string, string>
                {
                    ["city"] = "Unknown",
                    ["countryRegion"] = "",
                    ["country"] = "Unknown",
                    ["timezone"] = timezone
                }
            };

            if (!string.IsNullOrEmpty(title))
            {
                payload["title"] = title;
            }

            var json = JsonConvert.SerializeObject(payload);
            FileLogger.Instance.Info("MeetingSocket", $"Sending connect payload ({json.Length} chars)");
            await _transport.SendAsync(json);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingSocket", $"Connect payload send failed: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
        }
    }

    /// <summary>
    /// Send audio data chunk as JSON payload.
    /// </summary>
    public async Task SendAudioData(byte[] audioData, string source = "mic")
    {
        if (!_isConnectionConfirmed)
        {
            // Buffer audio until connection is confirmed
            _pendingAudioBuffer.Add((audioData, source));
            return;
        }

        try
        {
            var payload = new Dictionary<string, object>
            {
                ["type"] = "audio_data",
                ["source"] = source,
                ["data"] = new Dictionary<string, object>
                {
                    ["audio_data"] = Convert.ToBase64String(audioData),
                    ["sample_rate"] = 16000
                }
            };

            var json = JsonConvert.SerializeObject(payload);
            await _transport.SendAsync(json);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingSocket", $"Audio data send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Send final signal when meeting ends.
    /// </summary>
    public async Task SendFinalSignal()
    {
        try
        {
            var payload = JsonConvert.SerializeObject(new { type = "trigger.transcript.isFinal" });
            await _transport.SendAsync(payload);
            FileLogger.Instance.Info("MeetingSocket", "Sent final transcript signal");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingSocket", $"Final signal send failed: {ex.Message}");
        }
    }

    private async void FlushPendingAudio()
    {
        foreach (var (data, source) in _pendingAudioBuffer)
        {
            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["type"] = "audio_data",
                    ["source"] = source,
                    ["data"] = new Dictionary<string, object>
                    {
                        ["audio_data"] = Convert.ToBase64String(data),
                        ["sample_rate"] = 16000
                    }
                };

                var json = JsonConvert.SerializeObject(payload);
                await _transport.SendAsync(json);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warning("MeetingSocket", $"Buffer flush send failed: {ex.Message}");
            }
        }
        _pendingAudioBuffer.Clear();
        FileLogger.Instance.Info("MeetingSocket", "Flushed pending audio buffer");
    }

    private void HandleMessage(object? sender, string message)
    {
        try
        {
            var json = JObject.Parse(message);
            FileLogger.Instance.Info("MeetingSocket",
                $"Received: {message.Substring(0, Math.Min(200, message.Length))}");

            // Connection confirmation
            if (json.ContainsKey("event") && json["event"]?.ToString() == "connect")
            {
                _isConnectionConfirmed = true;
                FileLogger.Instance.Info("MeetingSocket", "Connection confirmed by server");
                OnConnectionConfirmed?.Invoke(this, EventArgs.Empty);
                FlushPendingAudio();
                return;
            }

            // Error
            if (json.ContainsKey("type") && json["type"]?.ToString() == "error")
            {
                var error = json["message"]?.ToString() ?? "Meeting error";
                OnError?.Invoke(this, error);
                return;
            }

            // Transcription messages
            var type = json["type"]?.ToString();
            string? text = null;
            string? speaker = null;
            string? source = null;
            bool isFinal = false;

            if (type == "transcription" || type == "partial_transcription" ||
                json.ContainsKey("text") || json.ContainsKey("transcript") ||
                json.ContainsKey("data"))
            {
                // Try multiple formats (macOS supports 3 different formats)
                if (json.ContainsKey("data") && json["data"] is JObject dataObj)
                {
                    text = dataObj["text"]?.ToString();
                    speaker = dataObj["speaker"]?.ToString();
                    source = dataObj["source"]?.ToString();
                }
                else
                {
                    text = json["text"]?.ToString() ?? json["transcript"]?.ToString();
                    speaker = json["speaker"]?.ToString();
                    source = json["source"]?.ToString();
                }

                isFinal = json["is_final"]?.Value<bool>() ?? false;

                if (!string.IsNullOrEmpty(text))
                {
                    var transcription = new MeetingTranscription
                    {
                        Text = text,
                        Speaker = speaker ?? "Unknown",
                        Source = source ?? "mic",
                        IsFinal = isFinal
                    };

                    if (type == "partial_transcription" || !isFinal)
                    {
                        OnPartialTranscription?.Invoke(this, transcription);
                    }
                    else
                    {
                        OnTranscription?.Invoke(this, transcription);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingSocket",
                $"Parse error: {ex.Message} - Raw: {message.Substring(0, Math.Min(100, message.Length))}");
        }
    }
}

public class MeetingTranscription
{
    public string Text { get; set; } = "";
    public string Speaker { get; set; } = "";
    public string Source { get; set; } = "mic";
    public bool IsFinal { get; set; }
}
