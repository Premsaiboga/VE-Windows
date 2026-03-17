using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Models;

namespace VE.Windows.Services;

/// <summary>
/// Voice service — matches macOS VoiceService.
/// Handles Dictionary (vocabulary/replacement words) and Snippets via GraphQL.
/// Also provides prediction/dictation logs.
/// </summary>
public sealed class VoiceService
{
    public static VoiceService Instance { get; } = new();
    private VoiceService() { }

    private string? GetGraphQLUrl()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("meeting_api");
        var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(workspaceId)) return null;
        return $"{baseUrl}/{workspaceId}/graphql";
    }

    // --- Dictionary ---

    public async Task<List<DictionaryWord>> ListWords(string? type = null)
    {
        try
        {
            var url = GetGraphQLUrl();
            if (url == null) return new();

            var variables = type != null ? new { type } : (object)new { };
            var payload = new
            {
                query = @"query ListWords($type: String) {
                    listWords(type: $type) {
                        success
                        data {
                            _id
                            word
                            replacement
                            source
                            type
                        }
                    }
                }",
                variables
            };

            var response = await NetworkService.Instance.PostRawAsync(url, payload);
            if (response == null) return new();

            var json = JObject.Parse(response);
            var data = json["data"]?["listWords"]?["data"] as JArray;
            if (data == null) return new();

            var result = new List<DictionaryWord>();
            foreach (var item in data)
            {
                result.Add(new DictionaryWord
                {
                    Id = item["_id"]?.ToString() ?? "",
                    Word = item["word"]?.ToString() ?? "",
                    Replacement = item["replacement"]?.ToString() ?? "",
                    Source = item["source"]?.ToString() ?? "user",
                    Type = item["type"]?.ToString() ?? "vocabulary"
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("VoiceService", $"ListWords failed: {ex.Message}");
            return new();
        }
    }

    public async Task<bool> CreateOrUpdateWord(string word, string replacement, string type, string? wordId = null)
    {
        try
        {
            var url = GetGraphQLUrl();
            if (url == null) return false;

            var input = new Dictionary<string, object>
            {
                ["word"] = word,
                ["replacement"] = replacement,
                ["type"] = type,
                ["source"] = "user"
            };
            if (wordId != null) input["id"] = wordId;

            var payload = new
            {
                query = @"mutation CreateOrUpdateWord($input: WordItemInput!) {
                    createOrUpdateWord(input: $input) {
                        success
                        message
                    }
                }",
                variables = new { input }
            };

            var response = await NetworkService.Instance.PostRawAsync(url, payload);
            if (response == null) return false;

            var json = JObject.Parse(response);
            return json["data"]?["createOrUpdateWord"]?["success"]?.Value<bool>() ?? false;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("VoiceService", $"CreateOrUpdateWord failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteWord(string wordId)
    {
        try
        {
            var url = GetGraphQLUrl();
            if (url == null) return false;

            var payload = new
            {
                query = @"mutation DeleteWord($wordId: ID!) {
                    deleteWord(wordId: $wordId) {
                        success
                    }
                }",
                variables = new { wordId }
            };

            var response = await NetworkService.Instance.PostRawAsync(url, payload);
            if (response == null) return false;

            var json = JObject.Parse(response);
            return json["data"]?["deleteWord"]?["success"]?.Value<bool>() ?? false;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("VoiceService", $"DeleteWord failed: {ex.Message}");
            return false;
        }
    }

    // --- Snippets (uses same GraphQL as dictionary with type="snippet") ---

    public async Task<List<DictionaryWord>> ListSnippets()
    {
        return await ListWords("snippet");
    }

    public async Task<bool> SaveSnippet(string shortcut, string content, string? snippetId = null)
    {
        return await CreateOrUpdateWord(shortcut, content, "snippet", snippetId);
    }

    public async Task<bool> DeleteSnippet(string snippetId)
    {
        return await DeleteWord(snippetId);
    }

    // --- Prediction Logs ---

    public async Task<List<PredictionLogItem>> GetPredictionLogs(int page = 1, int limit = 20)
    {
        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return new();

            var response = await NetworkService.Instance.GetRawAsync(
                $"{baseUrl}/{workspaceId}/ai-chat/list-multiagent-conversations?page={page}&limit={limit}&sortBy=createdAt&sortType=-1");
            if (response == null) return new();

            // Try basic parsing - prediction logs may come from a different endpoint
            var json = JObject.Parse(response);
            var data = json["data"] as JArray;
            if (data == null) return new();

            var result = new List<PredictionLogItem>();
            foreach (var item in data)
            {
                result.Add(new PredictionLogItem
                {
                    Id = item["_id"]?.ToString() ?? "",
                    Query = item["originalQuery"]?.ToString() ?? "",
                    Response = item["response"]?.ToString() ?? "",
                    Status = item["status"]?.ToString() ?? "",
                    CreatedAt = item["createdAt"]?.Value<long>() ?? 0,
                    App = item["windowTitle"]?.ToString() ?? item["app"]?.ToString() ?? ""
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("VoiceService", $"GetPredictionLogs failed: {ex.Message}");
            return new();
        }
    }
}
