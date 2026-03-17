using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;

namespace VE.Windows.Services;

/// <summary>
/// Intent Mail service — matches macOS IntentMailService.
/// Handles intent model config, email categories, draft settings, proactive emails.
/// </summary>
public sealed class IntentMailService
{
    public static IntentMailService Instance { get; } = new();
    private IntentMailService() { }

    private string? GetWhatsappUrl()
    {
        return BaseURLService.Instance.GetBaseUrl("whatsapp");
    }

    // --- Intent Model ---

    public async Task<(string? companionName, string? behaviour)> GetIntentModel()
    {
        try
        {
            var baseUrl = GetWhatsappUrl();
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return (null, null);

            var response = await NetworkService.Instance.GetRawAsync(
                $"{baseUrl}/email-tags/workspace/{workspaceId}/get-intent-score");
            if (response == null) return (null, null);

            var json = JObject.Parse(response);
            var name = json["companionName"]?.ToString() ?? json["data"]?["companionName"]?.ToString();
            var behaviour = json["behaviour"]?.ToString() ?? json["data"]?["behaviour"]?.ToString();
            return (name, behaviour);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("IntentMail", $"GetIntentModel failed: {ex.Message}");
            return (null, null);
        }
    }

    public async Task<bool> UpdateIntentModel(string companionName, string behaviour)
    {
        try
        {
            var baseUrl = GetWhatsappUrl();
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return false;

            var response = await NetworkService.Instance.PutRawAsync(
                $"{baseUrl}/email-tags/workspace/{workspaceId}/update-intent-model",
                new { companionName, behaviour });
            return response != null;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("IntentMail", $"UpdateIntentModel failed: {ex.Message}");
            return false;
        }
    }

    // --- Connected Email Accounts ---

    public async Task<List<ConnectedEmailAccount>> GetConnectedEmailAccounts()
    {
        try
        {
            var connected = await ConnectorsService.Instance.GetConnectedIntegrations();
            return connected
                .Where(c => c.App is "google" or "composio-gmail" or "outlookMail" or "gmail")
                .Where(c => c.IsActive)
                .Select(c => new ConnectedEmailAccount
                {
                    Id = c.Id,
                    Email = c.Email ?? "",
                    App = c.App,
                    IsActive = c.IsActive
                })
                .ToList();
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("IntentMail", $"GetConnectedEmailAccounts failed: {ex.Message}");
            return new();
        }
    }

    // --- Proactive Email Config ---

    public async Task<ProactiveEmailConfig?> GetProactiveEmailConfig(string connectedIntegrationId)
    {
        try
        {
            var baseUrl = GetWhatsappUrl();
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return null;

            var response = await NetworkService.Instance.GetRawAsync(
                $"{baseUrl}/email-tags/workspace/{workspaceId}/connected-integration/{connectedIntegrationId}/proactive-email-config");
            if (response == null) return null;

            var json = JObject.Parse(response);
            var config = new ProactiveEmailConfig
            {
                IsActive = json["isActive"]?.Value<bool>() ?? false
            };

            var draft = json["emailDraft"];
            if (draft != null)
            {
                config.Prompt = draft["prompt"]?.ToString();
                config.IsEnabled = draft["isEnabled"]?.Value<bool>() ?? false;
                config.ReplyFrequency = draft["replyFrequency"]?.Value<int>() ?? 0;
                config.MarketingIrrelevanceRatio = draft["marketingIrrelevanceRatio"]?.Value<int>() ?? 0;
                config.MarketingFilterSensitivity = draft["marketingFilterSensitivity"]?.Value<int>() ?? 0;
                config.RespectUserLabel = draft["respectUserLabel"]?.Value<bool>() ?? false;
                config.FollowAfter = draft["followAfter"]?.Value<int>() ?? 3;
                config.ProactiveRhythm = draft["proactiveRhythm"]?.Value<bool>() ?? false;
            }

            return config;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("IntentMail", $"GetProactiveEmailConfig failed: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> UpdateDraftPromptSettings(string connectedIntegrationId, ProactiveEmailConfig config)
    {
        try
        {
            var baseUrl = GetWhatsappUrl();
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return false;

            var response = await NetworkService.Instance.PutRawAsync(
                $"{baseUrl}/email-tags/workspace/{workspaceId}/connected-integration/{connectedIntegrationId}/update-draft-prompt",
                new
                {
                    isEnabled = config.IsEnabled,
                    replyFrequency = config.ReplyFrequency,
                    marketingIrrelevanceRatio = config.MarketingIrrelevanceRatio,
                    marketingFilterSensitivity = config.MarketingFilterSensitivity,
                    respectUserLabel = config.RespectUserLabel,
                    followAfter = config.FollowAfter,
                    prompt = config.Prompt ?? "",
                    proactiveRhythm = config.ProactiveRhythm
                });
            return response != null;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("IntentMail", $"UpdateDraftPromptSettings failed: {ex.Message}");
            return false;
        }
    }

    // --- Email Categories ---

    public async Task<List<EmailCategory>> GetEmailCategories(string connectedIntegrationId)
    {
        try
        {
            var baseUrl = GetWhatsappUrl();
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return new();

            var response = await NetworkService.Instance.GetRawAsync(
                $"{baseUrl}/email-tags/workspace/{workspaceId}/connected-integration/{connectedIntegrationId}/email-tags");
            if (response == null) return new();

            var json = JObject.Parse(response);
            var data = json["data"] as JArray ?? json as JArray;
            if (data == null)
            {
                // Try extracting from root object's array-like structure
                var rootArray = JArray.Parse(response);
                data = rootArray;
            }

            var result = new List<EmailCategory>();
            foreach (var item in data)
            {
                result.Add(new EmailCategory
                {
                    Id = item["_id"]?.ToString() ?? item["tagId"]?.ToString() ?? "",
                    TagId = item["tagId"]?.ToString() ?? item["_id"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    IsEnabled = item["isEnabled"]?.Value<bool>() ?? false,
                    Color = item["color"]?.ToString(),
                    Description = item["description"]?.ToString()
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("IntentMail", $"GetEmailCategories failed: {ex.Message}");
            return new();
        }
    }

    public async Task<bool> UpdateEmailCategories(string connectedIntegrationId, string app, List<EmailCategory> categories)
    {
        try
        {
            var baseUrl = GetWhatsappUrl();
            var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
            if (baseUrl == null || string.IsNullOrEmpty(workspaceId)) return false;

            // URL prefix varies by provider (matches macOS)
            var prefix = app switch
            {
                "composio-gmail" => "gmail-composio/email-tags",
                "google" or "gmail" => "google/email-tags",
                _ => "email-tags"
            };

            var tags = categories.Select(c => new { tagId = c.TagId, isEnabled = c.IsEnabled }).ToList();
            var response = await NetworkService.Instance.PutRawAsync(
                $"{baseUrl}/{prefix}/workspace/{workspaceId}/connected-integration/{connectedIntegrationId}/upsert-email-tags",
                new { emailTags = tags });
            return response != null;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("IntentMail", $"UpdateEmailCategories failed: {ex.Message}");
            return false;
        }
    }
}

// --- Models ---

public class ConnectedEmailAccount
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string App { get; set; } = "";
    public bool IsActive { get; set; }
    public string ProviderName => App switch
    {
        "google" or "gmail" or "composio-gmail" => "Gmail",
        "outlookMail" => "Outlook",
        _ => App
    };
}

public class ProactiveEmailConfig
{
    public bool IsActive { get; set; }
    public bool IsEnabled { get; set; }
    public int ReplyFrequency { get; set; }
    public int MarketingIrrelevanceRatio { get; set; }
    public int MarketingFilterSensitivity { get; set; }
    public bool RespectUserLabel { get; set; }
    public int FollowAfter { get; set; } = 3;
    public string? Prompt { get; set; }
    public bool ProactiveRhythm { get; set; }
}

public class EmailCategory
{
    public string Id { get; set; } = "";
    public string TagId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string? Color { get; set; }
    public string? Description { get; set; }
}
