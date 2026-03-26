using VE.Windows.Infrastructure;
using VE.Windows.Managers;

namespace VE.Windows.Services;

/// <summary>
/// Multi-region URL resolution matching macOS BaseURLService.
/// Production endpoints only.
/// </summary>
public sealed class BaseURLService : IBaseURLService
{
    public static BaseURLService Instance { get; } = new();

    private static readonly HashSet<string> GlobalTypes = new() { "auth", "delete-tenant" };

    private static readonly Dictionary<string, string> GlobalBaseUrls = new()
    {
        ["auth"] = "https://auth.ve.ai",
        ["delete-tenant"] = "https://us.api.ve.ai/delete-tenant/1.0",
        ["referral"] = "https://ve.ai/referral",
        ["twitter_share"] = "https://x.com/intent/post?text=",
        ["share_and_earn_kit"] = "https://veai.ve.ai/page/affiliate",
        ["desktop_login"] = "https://ve.ai/auth/desktop-login",
    };

    private BaseURLService() { }

    private static Dictionary<string, string> GetRegionBaseUrls(string region)
    {
        return region switch
        {
            "ap-south-1" => new Dictionary<string, string>
            {
                ["tenant"] = "https://ap.api.ve.ai/tenants/1.0",
                ["tenant-users"] = "https://ap.api.ve.ai/tenant-users/1.0",
                ["tenant_users_api"] = "https://ap.api.ve.ai/tenant-users/1.0",
                ["ai_assistant_api"] = "https://ap.api.ve.ai/agents/1.0",
                ["voice_enrollment_api"] = "wss://voice-intelligence.us-east-1.ve.ai",
                ["third_party_integrations_api"] = "https://ap.api.ve.ai/third-party-integrations/1.0",
                ["whatsapp"] = "https://ap.api.ve.ai/whatsapp/1.0",
                ["meeting_api"] = "https://ap.api.ve.ai/meeting/1.0",
                ["calendar_api"] = "https://ap.api.ve.ai/google/1.0",
                ["microsoft_integration_api"] = "https://ap.api.ve.ai/microsoft-integration/1.0",
                ["chat_ws_api"] = "wss://ai.us-east-1.ve.ai",
                ["guest_chat_ws_api"] = "wss://guestsearch.us-east-1.ve.ai",
                ["unified_audio_ws_api"] = "wss://cursor-intelligence.us-east-1.ve.ai",
                ["dictation_ws_api"] = "wss://voice-intelligence.us-east-1.ve.ai",
                ["voice_intelligence_ws_api"] = "wss://voice-intelligence.us-east-1.ve.ai",
                ["meeting_ws_api"] = "wss://recall.us-east-1.ve.ai",
                ["folders_api"] = "https://ap.api.ve.ai/folders/1.0",
                ["galleries_api"] = "https://ap.api.ve.ai/galleries/1.0",
            },
            _ => new Dictionary<string, string> // us-east-1 (default)
            {
                ["tenant"] = "https://us.api.ve.ai/tenants/1.0",
                ["tenant-users"] = "https://us.api.ve.ai/tenant-users/1.0",
                ["tenant_users_api"] = "https://us.api.ve.ai/tenant-users/1.0",
                ["ai_assistant_api"] = "https://us.api.ve.ai/agents/1.0",
                ["voice_enrollment_api"] = "wss://voice-intelligence.us-east-1.ve.ai",
                ["third_party_integrations_api"] = "https://us.api.ve.ai/third-party-integrations/1.0",
                ["whatsapp"] = "https://us.api.ve.ai/whatsapp/1.0",
                ["meeting_api"] = "https://us.api.ve.ai/meeting/1.0",
                ["calendar_api"] = "https://us.api.ve.ai/google/1.0",
                ["microsoft_integration_api"] = "https://us.api.ve.ai/microsoft-integration/1.0",
                ["chat_ws_api"] = "wss://ai.us-east-1.ve.ai",
                ["guest_chat_ws_api"] = "wss://guestsearch.us-east-1.ve.ai",
                ["unified_audio_ws_api"] = "wss://cursor-intelligence.us-east-1.ve.ai",
                ["dictation_ws_api"] = "wss://voice-intelligence.us-east-1.ve.ai",
                ["voice_intelligence_ws_api"] = "wss://voice-intelligence.us-east-1.ve.ai",
                ["meeting_ws_api"] = "wss://recall.us-east-1.ve.ai",
                ["folders_api"] = "https://us.api.ve.ai/folders/1.0",
                ["galleries_api"] = "https://us.api.ve.ai/galleries/1.0",
            }
        };
    }

    public string? GetBaseUrl(string type, string? region = null)
    {
        if (GlobalTypes.Contains(type))
        {
            return GlobalBaseUrls.GetValueOrDefault(type);
        }

        var regionValue = region ?? AuthManager.Instance.Storage.Region ?? "us-east-1";
        var regionUrls = GetRegionBaseUrls(regionValue);
        return regionUrls.GetValueOrDefault(type);
    }

    public string? GetGlobalUrl(string type)
    {
        return GlobalBaseUrls.GetValueOrDefault(type);
    }

    public string[] AssetUrlCandidates(string path, string region)
    {
        return new[]
        {
            $"https://assets.ve.ai/{path}",
            $"https://{region}.assets.ve.ai/{path}",
            $"https://{path}",
        };
    }
}
