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

    // --- List Meetings ---

    public async Task<List<MeetingListItem>> ListMeetings(int page = 1, int limit = 20, string? pageType = null)
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
            if (data == null) return new();

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
            if (summaryRaw == null) return null;

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

            FileLogger.Instance.Info("MeetingGQL", $"Got summary for {meetingId}");
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
            if (response == null) return null;

            var json = JObject.Parse(response);
            var analyticsRaw = json["data"]?["getMeetingAnalytics"];
            if (analyticsRaw == null) return null;

            JObject analyticsObj;
            if (analyticsRaw.Type == JTokenType.String)
                analyticsObj = JObject.Parse(analyticsRaw.Value<string>()!);
            else
                analyticsObj = (JObject)analyticsRaw;

            var result = MeetingAnalyticsData.ParseFromJson(analyticsObj);
            FileLogger.Instance.Info("MeetingGQL", $"Got analytics for {meetingId}: generated={result.AnalyticsGenerated}");
            return result;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("MeetingGQL", $"GetMeetingAnalytics failed: {ex.Message}");
            return null;
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