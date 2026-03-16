using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Models;

namespace VE.Windows.Services;

/// <summary>
/// Calendar service for upcoming meetings.
/// Matches macOS CalendarService: GET {calendar_api}/{workspaceId}/calendar/getAllEvents
/// </summary>
public sealed class CalendarService
{
    public static CalendarService Instance { get; } = new();
    private CalendarService() { }

    /// <summary>
    /// Fetch upcoming calendar events. Matches macOS exactly:
    /// GET {calendar_api}/{workspaceId}/calendar/getAllEvents?limit={limit}&page={page}&startDate={ISO8601}&sortOrder=asc
    /// </summary>
    public async Task<List<UpcomingMeetingItem>> GetUpcomingMeetings(int limit = 10, int page = 1)
    {
        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("calendar_api");
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(workspaceId))
            {
                FileLogger.Instance.Warning("Calendar", "No calendar_api URL or workspaceId");
                return new();
            }

            var startDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var url = $"{baseUrl}/{workspaceId}/calendar/getAllEvents?limit={limit}&page={page}&startDate={startDate}&sortOrder=asc";

            FileLogger.Instance.Info("Calendar", $"Fetching upcoming meetings: {url}");
            var response = await NetworkService.Instance.GetRawAsync(url);
            if (response == null)
            {
                FileLogger.Instance.Warning("Calendar", "No response from calendar API");
                return new();
            }

            FileLogger.Instance.Debug("Calendar", $"Calendar response ({response.Length} chars): {response.Substring(0, Math.Min(300, response.Length))}");

            var json = JObject.Parse(response);
            var data = json["data"]?["data"] as JArray;
            if (data == null)
            {
                FileLogger.Instance.Warning("Calendar", $"No calendar data. Response: {response.Substring(0, Math.Min(200, response.Length))}");
                return new();
            }

            var items = new List<UpcomingMeetingItem>();
            foreach (var item in data)
            {
                var startStr = item["startDateTime"]?.ToString();
                var endStr = item["endDateTime"]?.ToString();

                if (string.IsNullOrEmpty(startStr)) continue;

                if (!DateTime.TryParse(startStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var startTime))
                    continue;
                DateTime.TryParse(endStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var endTime);

                // Filter: only show meetings from today onwards
                if (startTime.ToLocalTime().Date < DateTime.Today) continue;

                var appSource = item["app"]?.ToString()?.ToLower() ?? "";
                items.Add(new UpcomingMeetingItem
                {
                    Id = item["_id"]?.ToString() ?? "",
                    Title = item["title"]?.ToString() ?? "Untitled",
                    StartTime = startTime.ToLocalTime(),
                    EndTime = endTime.ToLocalTime(),
                    MeetingUrl = item["meetingLink"]?.ToString(),
                    Organizer = item["organizer"]?.ToString(),
                    Source = appSource == "outlook" ? "outlook" : "google",
                    Attendees = item["attendees"]?.Select(a => a["email"]?.ToString() ?? "").Where(e => !string.IsNullOrEmpty(e)).ToList() ?? new()
                });
            }

            // Sort by start time ascending (soonest first)
            items.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            FileLogger.Instance.Info("Calendar", $"Loaded {items.Count} upcoming meetings");
            return items;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Calendar", $"GetUpcomingMeetings failed: {ex.Message}");
            return new();
        }
    }
}
