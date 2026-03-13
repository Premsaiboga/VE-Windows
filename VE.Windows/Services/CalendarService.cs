using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Services;

public sealed class CalendarService
{
    public static CalendarService Instance { get; } = new();
    private CalendarService() { }

    public async Task<UpcomingMeetingsResponse?> GetUpcomingMeetings(int limit = 10, int page = 1)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("calendar_api");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<UpcomingMeetingsResponse>(
            $"{baseUrl}/calendar/events/upcoming?limit={limit}&page={page}");
    }
}
