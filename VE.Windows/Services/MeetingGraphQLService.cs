using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Models;

namespace VE.Windows.Services;

/// <summary>
/// GraphQL service for meeting list, summary, transcriptions, and analytics.
/// Matches macOS MeetingQueries + GraphqlActions.
/// </summary>
public sealed class MeetingGraphQLService
{
    public static MeetingGraphQLService Instance { get; } = new();
    private MeetingGraphQLService() { }

    private string? GetGraphQLUrl()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("meeting_api");
        var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(workspaceId)) return null;
        return $"{baseUrl}/{workspaceId}/graphql";
    }

    // --- List All Meetings (auto-paginates) ---

    public async Task<List<MeetingListItem>> ListAllMeetings()
    {
        var allMeetings = new List<MeetingListItem>();
        int page = 1;
        const int limit = 50;

        while (true)
        {
            var pageMeetings = await ListMeetings(page, limit);
            if (pageMeetings.Count == 0) break;

            allMeetings.AddRange(pageMeetings);

            if (pageMeetings.Count < limit) break; // Last page
            page++;

            // Safety cap to prevent infinite loop
            if (page > 20) break;
        }

        FileLogger.Instance.Info("MeetingGQL", $"Loaded {allMeetings.Count} total meetings across {page} pages");
        return allMeetings;
    }

    // --- List Meetings (single page) ---

    public async Task<List<MeetingListItem>> ListMeetings(int page = 1, int limit = 50, string? pageType = null)
    {
        try
        {
            var url = GetGraphQLUrl();
            if (url == null) return new();

            var payload = new
            {
                query = @"query Query($limit: Int!, $page: Int!, $pageType: String) {
                    listMeetings(limit: $limit, page: $page, pageType: $pageType) {
                        currentPage
                        data {
                            createdAt
                            title
                            _id
                            status
                            isTranscription
                        }
                    }
                }",
                variables = new { limit, page, pageType }
            };

            var response = await NetworkService.Instance.PostRawAsync(url, payload);
            if (response == null) return new();

            var json = JObject.Parse(response);
            var data = json["data"]?["listMeetings"]?["data"] as JArray;
            if (data == null)
            {
                FileLogger.Instance.Warning("MeetingGQL", $"ListMeetings: no data in response. Errors: {json["errors"]}");
                return new();
            }

            var meetings = new List<MeetingListItem>();
            foreach (var item in data)
            {
                meetings.Add(new MeetingListItem
                {
                    Id = item["_id"]?.Value<string>() ?? "",
                    Title = item["title"]?.Value<string>() ?? "Untitled Meeting",
                    CreatedAt = item["createdAt"]?.Value<double>() ?? 0,
                    Status = item["status"]?.Value<string>() ?? "",
                    IsTranscription = item["isTranscription"]?.Value<bool>() ?? false
                });
            }

            FileLogger.Instance.Info("MeetingGQL", $"Listed {meetings.Count} meetings (page {page})");
            return meetings;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingGQL", $"ListMeetings failed: {ex.Message}");
            return new();
        }
    }

    // --- Get Meeting Summary ---

    public async Task<MeetingSummaryData?> GetMeetingSummary(string meetingId)
    {
        try
        {
            var url = GetGraphQLUrl();
            if (url == null) return null;

            var payload = new
            {
                query = @"query Query($meetingId: ID!) {
                    getMeetingSummary(meetingId: $meetingId)
                }",
                variables = new { meetingId }
            };

            var response = await NetworkService.Instance.PostRawAsync(url, payload);
            if (response == null) return null;

            var json = JObject.Parse(response);
            var summaryRaw = json["data"]?["getMeetingSummary"];
            if (summaryRaw == null)
            {
                FileLogger.Instance.Warning("MeetingGQL", $"GetMeetingSummary: null response for {meetingId}. Errors: {json["errors"]}");
                return null;
            }

            // The response is a JSON string that needs to be parsed
            JObject summaryObj;
            if (summaryRaw.Type == JTokenType.String)
                summaryObj = JObject.Parse(summaryRaw.Value<string>()!);
            else
                summaryObj = (JObject)summaryRaw;

            var result = new MeetingSummaryData();

            if (summaryObj["meetingData"] is JObject meetingData)
            {
                result.MeetingData = new MeetingData
                {
                    Id = meetingData["_id"]?.Value<string>(),
                    Title = meetingData["title"]?.Value<string>(),
                    CreatedAt = meetingData["createdAt"]?.Value<double>(),
                    Status = meetingData["status"]?.Value<string>(),
                    TranscriptionSource = meetingData["transcriptionSource"]?.Value<string>()
                };
                result.TranscriptionSummary = meetingData["transcriptionSummary"]?.Value<string>();
            }

            FileLogger.Instance.Info("MeetingGQL", $"Got summary for {meetingId}, hasMeetingData={result.MeetingData != null}, hasSummary={result.TranscriptionSummary != null}");
            return result;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingGQL", $"GetMeetingSummary failed: {ex.Message}");
            return null;
        }
    }

    // --- List Transcriptions ---

    public async Task<List<TranscriptionItem>> ListTranscriptions(string meetingId, int page = 1, int limit = 100)
    {
        try
        {
            var url = GetGraphQLUrl();
            if (url == null) return new();

            var payload = new
            {
                query = @"query ListTranscriptions($meetingId: ID!, $limit: Int!, $page: Int!) {
                    listTranscriptions(meetingId: $meetingId, limit: $limit, page: $page) {
                        data {
                            _id
                            meetingId
                            speakerName
                            transcript
                            createdAt
                        }
                    }
                }",
                variables = new { meetingId, limit, page }
            };

            var response = await NetworkService.Instance.PostRawAsync(url, payload);
            if (response == null) return new();

            var json = JObject.Parse(response);
            var data = json["data"]?["listTranscriptions"]?["data"] as JArray;
            if (data == null) return new();

            var items = new List<TranscriptionItem>();
            foreach (var item in data)
            {
                items.Add(new TranscriptionItem
                {
                    Id = item["_id"]?.Value<string>() ?? "",
                    MeetingId = item["meetingId"]?.Value<string>() ?? "",
                    SpeakerName = item["speakerName"]?.Value<string>() ?? "Speaker",
                    Transcript = item["transcript"]?.Value<string>() ?? "",
                    CreatedAt = item["createdAt"]?.Value<double>() ?? 0
                });
            }

            FileLogger.Instance.Info("MeetingGQL", $"Listed {items.Count} transcriptions for {meetingId}");
            return items;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingGQL", $"ListTranscriptions failed: {ex.Message}");
            return new();
        }
    }

    // --- Get Meeting Analytics ---

    public async Task<MeetingAnalyticsData?> GetMeetingAnalytics(string meetingId)
    {
        try
        {
            var url = GetGraphQLUrl();
            if (url == null) return null;

            var payload = new
            {
                query = @"query Query($meetingId: ID!) {
                    getMeetingAnalytics(meetingId: $meetingId)
                }",
                variables = new { meetingId }
            };

            var response = await NetworkService.Instance.PostRawAsync(url, payload);
            if (response == null)
            {
                FileLogger.Instance.Warning("MeetingGQL", $"GetMeetingAnalytics: null response for {meetingId}");
                return null;
            }

            FileLogger.Instance.Debug("MeetingGQL", $"Analytics raw response for {meetingId}: {response.Substring(0, Math.Min(500, response.Length))}");

            var json = JObject.Parse(response);

            // Check for GraphQL errors
            if (json["errors"] != null)
            {
                FileLogger.Instance.Warning("MeetingGQL", $"Analytics errors for {meetingId}: {json["errors"]}");
                return new MeetingAnalyticsData { AnalyticsGenerated = false };
            }

            var analyticsRaw = json["data"]?["getMeetingAnalytics"];
            if (analyticsRaw == null || analyticsRaw.Type == JTokenType.Null)
            {
                FileLogger.Instance.Info("MeetingGQL", $"Analytics not available for {meetingId}");
                return new MeetingAnalyticsData { AnalyticsGenerated = false };
            }

            // Parse the analytics — could be a JSON string or direct object
            JObject analyticsObj;
            if (analyticsRaw.Type == JTokenType.String)
            {
                var rawStr = analyticsRaw.Value<string>()!;
                FileLogger.Instance.Debug("MeetingGQL", $"Analytics is JSON string, length={rawStr.Length}");
                analyticsObj = JObject.Parse(rawStr);
            }
            else
            {
                analyticsObj = (JObject)analyticsRaw;
            }

            // The analytics data may be wrapped in an "analytics" key
            // (macOS checks for analyticsDict["analytics"] wrapper)
            if (analyticsObj["analytics"] is JObject innerAnalytics &&
                analyticsObj["status"] != null)
            {
                // Response format: { status, message, analytics_generated, analytics: { ... } }
                var result = MeetingAnalyticsData.ParseFromJson(innerAnalytics);
                result.AnalyticsGenerated = analyticsObj["analytics_generated"]?.Value<bool>() ?? true;
                FileLogger.Instance.Info("MeetingGQL", $"Got wrapped analytics for {meetingId}: generated={result.AnalyticsGenerated}, chapters={result.Chapters.Count}, participants={result.Participants.Count}");
                return result;
            }
            else
            {
                // Direct format: { meeting_metadata, chapters, highlights, ... }
                var result = MeetingAnalyticsData.ParseFromJson(analyticsObj);
                FileLogger.Instance.Info("MeetingGQL", $"Got direct analytics for {meetingId}: generated={result.AnalyticsGenerated}, chapters={result.Chapters.Count}, participants={result.Participants.Count}");
                return result;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingGQL", $"GetMeetingAnalytics failed: {ex.Message}");
            return new MeetingAnalyticsData { AnalyticsGenerated = false };
        }
    }

    // --- Generate Summary ---

    public async Task<bool> GenerateMeetingSummary(string meetingId)
    {
        try
        {
            var url = GetGraphQLUrl();
            if (url == null) return false;

            var payload = new
            {
                query = @"mutation GenerateMeetingSummary($meetingId: ID!) {
                    generateMeetingSummary(meetingId: $meetingId)
                }",
                variables = new { meetingId }
            };

            var response = await NetworkService.Instance.PostRawAsync(url, payload);
            FileLogger.Instance.Info("MeetingGQL", $"Generate summary triggered for {meetingId}");
            return response != null;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingGQL", $"GenerateSummary failed: {ex.Message}");
            return false;
        }
    }

    // --- Delete Meeting ---

    public async Task<bool> DeleteMeeting(string meetingId)
    {
        try
        {
            var url = GetGraphQLUrl();
            if (url == null) return false;

            var payload = new
            {
                query = @"mutation Mutation($meetingId: ID!) {
                    deleteMeeting(meetingId: $meetingId) {
                        success
                        message
                    }
                }",
                variables = new { meetingId }
            };

            var response = await NetworkService.Instance.PostRawAsync(url, payload);
            if (response == null) return false;

            var json = JObject.Parse(response);
            var success = json["data"]?["deleteMeeting"]?["success"]?.Value<bool>() ?? false;
            FileLogger.Instance.Info("MeetingGQL", $"Delete meeting {meetingId}: {success}");
            return success;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingGQL", $"DeleteMeeting failed: {ex.Message}");
            return false;
        }
    }
}
