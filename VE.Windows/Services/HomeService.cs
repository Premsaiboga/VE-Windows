using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Services;

public sealed class HomeService
{
    public static HomeService Instance { get; } = new();
    private HomeService() { }

    public async Task<HomeData?> GetHomeData()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<HomeData>($"{baseUrl}/home");
    }

    public async Task<UsageStats?> GetUsageStats()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<UsageStats>($"{baseUrl}/usage/stats");
    }

    public async Task<List<AIQuestion>?> GetAIQuestions()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("ai_assistant_api");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<List<AIQuestion>>($"{baseUrl}/questions/suggested");
    }
}
