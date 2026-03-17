using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Models;

namespace VE.Windows.Services;

/// <summary>
/// Connectors service — matches macOS ConnectorsService.
/// Handles available integrations, connected accounts, OAuth flow, disconnect.
/// </summary>
public sealed class ConnectorsService
{
    public static ConnectorsService Instance { get; } = new();
    private ConnectorsService() { }

    // --- Fetch Available Integrations ---

    public async Task<List<AvailableIntegration>> GetAvailableIntegrations()
    {
        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("third_party_integrations_api");
            if (baseUrl == null) return GetStaticIntegrations();

            var response = await NetworkService.Instance.GetRawAsync(
                $"{baseUrl}/integrations?environment=production");
            if (response == null) return GetStaticIntegrations();

            var json = JObject.Parse(response);
            var data = json["data"] as JArray;
            if (data == null) return GetStaticIntegrations();

            var result = new List<AvailableIntegration>();
            foreach (var item in data)
            {
                result.Add(new AvailableIntegration
                {
                    Id = item["_id"]?.ToString() ?? item["id"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    Type = item["type"]?.ToString() ?? "",
                    Description = item["description"]?.ToString() ?? "",
                    Category = item["category"]?.ToString() ?? "",
                    IconName = item["iconName"]?.ToString() ?? ""
                });
            }

            FileLogger.Instance.Info("Connectors", $"Fetched {result.Count} available integrations");
            return result.Count > 0 ? result : GetStaticIntegrations();
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Connectors", $"GetAvailableIntegrations failed: {ex.Message}");
            return GetStaticIntegrations();
        }
    }

    // --- Fetch Connected Integrations ---

    public async Task<List<ConnectedIntegration>> GetConnectedIntegrations()
    {
        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("third_party_integrations_api");
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return new();

            var response = await NetworkService.Instance.GetRawAsync(
                $"{baseUrl}/connected-accounts-v2/{workspaceId}");
            if (response == null) return new();

            var json = JObject.Parse(response);
            var data = json["data"] as JArray;
            if (data == null) return new();

            var result = new List<ConnectedIntegration>();
            foreach (var item in data)
            {
                result.Add(new ConnectedIntegration
                {
                    Id = item["_id"]?.ToString() ?? "",
                    Email = item["email"]?.ToString(),
                    App = item["app"]?.ToString() ?? "",
                    IsActive = item["isActive"]?.Value<bool>() ?? false,
                    Access = item["access"]?.ToString() ?? "private",
                    SyncedCount = item["syncedCount"]?.Value<int>() ?? 0,
                    AddedBy = item["tenantUser"]?["name"]?.ToString()
                });
            }

            FileLogger.Instance.Info("Connectors", $"Fetched {result.Count} connected integrations");
            return result;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Connectors", $"GetConnectedIntegrations failed: {ex.Message}");
            return new();
        }
    }

    // --- Connect Integration (OAuth) ---

    public async Task<string?> ConnectIntegration(string type, string access = "private")
    {
        try
        {
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (string.IsNullOrEmpty(workspaceId)) return null;

            string? baseUrl;
            string url;

            if (type == "google")
            {
                baseUrl = BaseURLService.Instance.GetBaseUrl("calendar_api");
                if (baseUrl == null) return null;
                url = $"{baseUrl}/{workspaceId}/auth?access={access}&isApp=true";
            }
            else if (type == "outlookMail")
            {
                baseUrl = BaseURLService.Instance.GetBaseUrl("microsoft_integration_api");
                if (baseUrl == null) return null;
                url = $"{baseUrl}/outlookmail/{workspaceId}/auth?access={access}";
            }
            else
            {
                baseUrl = BaseURLService.Instance.GetBaseUrl("third_party_integrations_api");
                if (baseUrl == null) return null;
                url = $"{baseUrl}/{type}/{workspaceId}/auth?access={access}";
            }

            var response = await NetworkService.Instance.GetRawAsync(url);
            if (response == null) return null;

            var json = JObject.Parse(response);
            var redirectUrl = json["redirectUrl"]?.ToString() ?? json["connectUrl"]?.ToString();
            return redirectUrl;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Connectors", $"ConnectIntegration failed: {ex.Message}");
            return null;
        }
    }

    // --- Disconnect Integration ---

    public async Task<bool> DisconnectIntegration(string type, string integrationId)
    {
        try
        {
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (string.IsNullOrEmpty(workspaceId)) return false;

            string? baseUrl;
            string prefix;

            if (type == "google")
            {
                baseUrl = BaseURLService.Instance.GetBaseUrl("calendar_api");
                prefix = "google";
            }
            else if (type == "outlookMail")
            {
                baseUrl = BaseURLService.Instance.GetBaseUrl("microsoft_integration_api");
                prefix = "outlookmail";
            }
            else
            {
                baseUrl = BaseURLService.Instance.GetBaseUrl("third_party_integrations_api");
                prefix = type;
            }

            if (baseUrl == null) return false;

            var response = await NetworkService.Instance.PutRawAsync(
                $"{baseUrl}/{prefix}/{workspaceId}/{integrationId}/deactivate-integration");

            FileLogger.Instance.Info("Connectors", $"Disconnected {type}/{integrationId}");
            return response != null;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Connectors", $"DisconnectIntegration failed: {ex.Message}");
            return false;
        }
    }

    // --- Static Fallback ---

    private static List<AvailableIntegration> GetStaticIntegrations() => new()
    {
        new() { Id = "1", Name = "Google", Type = "google", Category = "mail", Description = "Connect Gmail and Google Calendar" },
        new() { Id = "2", Name = "Outlook", Type = "outlookMail", Category = "mail", Description = "Connect Outlook Mail and Calendar" },
        new() { Id = "3", Name = "Granola", Type = "granola", Category = "meetings", Description = "Connect meeting notes from Granola" },
    };
}
