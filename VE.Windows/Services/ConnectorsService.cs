using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Services;

public sealed class ConnectorsService
{
    public static ConnectorsService Instance { get; } = new();
    private ConnectorsService() { }

    public async Task<List<ConnectorInfo>?> GetConnectors()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("third_party_integrations_api");
        if (baseUrl == null) return null;
        return await NetworkService.Instance.GetAsync<List<ConnectorInfo>>($"{baseUrl}/connectors");
    }

    public async Task<bool> DisconnectConnector(string connectorId)
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("third_party_integrations_api");
        if (baseUrl == null) return false;
        return await NetworkService.Instance.DeleteAsync($"{baseUrl}/connectors/{connectorId}");
    }

    public async Task ConnectGoogle()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("third_party_integrations_api");
        if (baseUrl == null) return;
        var response = await NetworkService.Instance.GetAsync<ConnectUrlResponse>($"{baseUrl}/google/auth-url");
        if (response?.Url != null)
        {
            AppURLs.OpenUrl(response.Url);
        }
    }

    public async Task ConnectMicrosoft()
    {
        var baseUrl = BaseURLService.Instance.GetBaseUrl("microsoft_integration_api");
        if (baseUrl == null) return;
        var response = await NetworkService.Instance.GetAsync<ConnectUrlResponse>($"{baseUrl}/auth-url");
        if (response?.Url != null)
        {
            AppURLs.OpenUrl(response.Url);
        }
    }

    private class ConnectUrlResponse
    {
        public string? Url { get; set; }
    }
}
