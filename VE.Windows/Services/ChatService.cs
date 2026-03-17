using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Models;

namespace VE.Windows.Services;

/// <summary>
/// Chat REST service — matches macOS ChatManager REST endpoints.
/// Lists recent chat sessions, fetches message history, deletes sessions.
/// </summary>
public sealed class ChatService
{
    public static ChatService Instance { get; } = new();
    private ChatService() { }

    // --- List Recent Sessions ---

    public async Task<(List<RecentChatItem> items, bool hasMore)> ListRecentSessions(int page = 1, int limit = 10)
    {
        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("tenant");
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return (new(), false);

            var response = await NetworkService.Instance.GetRawAsync(
                $"{baseUrl}/{workspaceId}/list-multiagent-sessions?page={page}&limit={limit}&title=&excludeAgentType=true&agentType[]=instruction_agent");
            if (response == null) return (new(), false);

            var json = JObject.Parse(response);
            var data = json["data"] as JArray;
            var hasMore = json["hasNextPage"]?.Value<bool>() ?? false;
            if (data == null) return (new(), false);

            var items = new List<RecentChatItem>();
            foreach (var item in data)
            {
                var createdAtVal = item["createdAt"];
                DateTime? createdAt = null;
                if (createdAtVal != null)
                {
                    if (createdAtVal.Type == JTokenType.Integer || createdAtVal.Type == JTokenType.Float)
                    {
                        var ts = createdAtVal.Value<long>();
                        createdAt = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
                    }
                }

                items.Add(new RecentChatItem
                {
                    Id = item["_id"]?.ToString() ?? "",
                    Title = item["title"]?.ToString() ?? "Untitled Chat",
                    Timestamp = createdAt ?? DateTime.UtcNow
                });
            }

            FileLogger.Instance.Info("ChatService", $"Listed {items.Count} recent sessions (page {page})");
            return (items, hasMore);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatService", $"ListRecentSessions failed: {ex.Message}");
            return (new(), false);
        }
    }

    // --- Fetch Messages in Session ---

    public async Task<List<ChatHistoryMessage>> GetSessionMessages(string sessionId, int page = 1, int limit = 20)
    {
        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return new();

            var response = await NetworkService.Instance.GetRawAsync(
                $"{baseUrl}/{workspaceId}/ai-chat/list-multiagent-conversations/{sessionId}?page={page}&limit={limit}&sortBy=createdAt&sortType=-1");
            if (response == null) return new();

            var json = JObject.Parse(response);
            var data = json["data"] as JArray;
            if (data == null) return new();

            var messages = new List<ChatHistoryMessage>();
            foreach (var item in data)
            {
                var query = item["originalQuery"]?.ToString() ?? "";
                var responseText = item["response"]?.ToString() ?? "";
                var status = item["status"]?.ToString() ?? "completed";

                // Parse citations
                var citations = new List<Citation>();
                var citationsArr = item["citations"] as JArray;
                if (citationsArr != null)
                {
                    foreach (var c in citationsArr)
                    {
                        citations.Add(new Citation
                        {
                            Title = c["title"]?.ToString() ?? "",
                            Url = c["url"]?.ToString() ?? "",
                            Snippet = c["snippet"]?.ToString() ?? ""
                        });
                    }
                }

                messages.Add(new ChatHistoryMessage
                {
                    Query = query,
                    Response = responseText,
                    Status = status,
                    Citations = citations
                });
            }

            return messages;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatService", $"GetSessionMessages failed: {ex.Message}");
            return new();
        }
    }

    // --- Delete Session ---

    public async Task<bool> DeleteSession(string sessionId)
    {
        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return false;

            var response = await NetworkService.Instance.DeleteRawAsync(
                $"{baseUrl}/{workspaceId}/ai-chat/delete-multiagent-conversation/{sessionId}",
                new { isPermanent = true });
            return response != null;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("ChatService", $"DeleteSession failed: {ex.Message}");
            return false;
        }
    }
}

// --- Models ---

public class ChatHistoryMessage
{
    public string Query { get; set; } = "";
    public string Response { get; set; } = "";
    public string Status { get; set; } = "completed";
    public List<Citation> Citations { get; set; } = new();
}
