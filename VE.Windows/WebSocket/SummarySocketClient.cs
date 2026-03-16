using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.WebSocket;

/// <summary>
/// Summary WebSocket client — connects to wss://meetings.us-east-1.ve.ai/genarateSummary/ws/{meetingId}?token=...
/// Sends trigger payload, receives streaming summary chunks, then final analytics.
/// Matches macOS MeetingSummaryView message parsing.
/// </summary>
public class SummarySocketClient
{
    private readonly WebSocketTransport _transport;
    private string _streamingBuffer = "";

    public event EventHandler<MeetingAnalyticsData>? OnSummaryComplete;
    public event EventHandler? OnSummarySkipped;
    public event EventHandler<string>? OnError;

    public bool IsConnected => _transport.IsConnected;

    public SummarySocketClient(WebSocketTransport transport)
    {
        _transport = transport;
        _transport.MessageReceived += HandleMessage;
    }

    /// <summary>
    /// Send trigger payload to start summary generation.
    /// Matches macOS: { type, session_id, tenant_id }
    /// </summary>
    public async Task SendTriggerPayload(string sessionId, string tenantId)
    {
        try
        {
            var payload = JsonConvert.SerializeObject(new
            {
                type = "trigger.transcript.generateSummary",
                session_id = sessionId,
                tenant_id = tenantId
            });
            FileLogger.Instance.Info("SummaryWS", $"Sending trigger payload");
            await _transport.SendAsync(payload);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("SummaryWS", $"Trigger send failed: {ex.Message}");
            OnError?.Invoke(this, ex.Message);
        }
    }

    private void HandleMessage(object? sender, string message)
    {
        try
        {
            FileLogger.Instance.Debug("SummaryWS", $"Received: {message.Substring(0, Math.Min(200, message.Length))}");

            var json = JObject.Parse(message);
            var eventType = json["event"]?.ToString();

            if (eventType != "meeting_analytics") return;

            var msg = json["message"];
            if (msg == null) return;

            // Message can be a string (streaming chunk) or object (done signal)
            if (msg.Type == JTokenType.String)
            {
                // Streaming chunk — accumulate buffer
                _streamingBuffer += msg.ToString();
                return;
            }

            if (msg.Type == JTokenType.Object)
            {
                var msgObj = (JObject)msg;
                var done = msgObj["done"]?.Value<bool>() ?? false;

                if (!done) return;

                // Check if analytics generation was skipped
                var analyticsGenerated = msgObj["analytics_generated"]?.Value<bool>();
                if (analyticsGenerated == false)
                {
                    FileLogger.Instance.Info("SummaryWS", "Summary generation skipped (insufficient transcription)");
                    OnSummarySkipped?.Invoke(this, EventArgs.Empty);
                    return;
                }

                // Parse analytics from done message or accumulated buffer
                JObject? analyticsJson = null;

                // Try from done message first
                if (msgObj["analytics"] is JObject directAnalytics)
                {
                    analyticsJson = directAnalytics;
                }
                // Fallback to accumulated buffer
                else if (!string.IsNullOrEmpty(_streamingBuffer))
                {
                    try { analyticsJson = JObject.Parse(_streamingBuffer); }
                    catch { FileLogger.Instance.Warning("SummaryWS", "Failed to parse streaming buffer"); }
                }

                if (analyticsJson != null)
                {
                    var result = MeetingAnalyticsData.ParseFromJson(analyticsJson);
                    result.AnalyticsGenerated = true;
                    FileLogger.Instance.Info("SummaryWS", $"Summary complete: chapters={result.Chapters.Count}, participants={result.Participants.Count}");
                    OnSummaryComplete?.Invoke(this, result);
                }
                else
                {
                    FileLogger.Instance.Warning("SummaryWS", "Done received but no analytics data");
                    OnSummarySkipped?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("SummaryWS", $"Parse error: {ex.Message}");
        }
    }

    public void ResetBuffer()
    {
        _streamingBuffer = "";
    }
}
