using VE.Windows.Models;

namespace VE.Windows.Services;

public sealed class SubscriptionService
{
    public static SubscriptionService Instance { get; } = new();
    private SubscriptionService() { }

    public async Task<SubscriptionInfo?> GetSubscription()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("tenant");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<SubscriptionInfo>($"{baseUrl}/subscription");
    }
}
