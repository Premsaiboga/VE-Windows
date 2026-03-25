namespace VE.Windows.Models;

public class UserProfile
{
    public string Id { get; set; } = "";
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? Role { get; set; }
    public string? Region { get; set; }
    public string? WorkspaceId { get; set; }
    public string? TenantId { get; set; }
    public bool IsOnboard { get; set; }
}

public class TenantInfo
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Domain { get; set; }
    public string? Plan { get; set; }
    public string? Region { get; set; }
    public string? WorkspaceMode { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? CompanyType { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
}

public class WorkspaceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? OwnerId { get; set; }
    public int MemberCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SubscriptionInfo
{
    public string Plan { get; set; } = "free";
    public string Status { get; set; } = "active";
    public DateTime? ExpiresAt { get; set; }
    public int? SeatsUsed { get; set; }
    public int? SeatsTotal { get; set; }
    public SubscriptionFeatures Features { get; set; } = new();
}

public class SubscriptionFeatures
{
    public bool Predictions { get; set; } = true;
    public bool Dictation { get; set; } = true;
    public bool Chat { get; set; } = true;
    public bool MeetingNotes { get; set; }
    public bool KnowledgeBase { get; set; }
    public bool Connectors { get; set; }
}

public class TeamMember
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "member";
    public string? Avatar { get; set; }
    public DateTime JoinedAt { get; set; }

    public string Initials
    {
        get
        {
            var parts = Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return Email.Length > 0 ? Email[..1].ToUpper() : "?";
            if (parts.Length == 1) return parts[0][..1].ToUpper();
            return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
        }
    }

    public string RoleDisplay => Role?.ToLowerInvariant() == "owner"
        ? "Owner"
        : $"Admin \u25BE";

    public System.Windows.Media.Brush RoleBrush => Role?.ToLowerInvariant() == "owner"
        ? new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007CEC"))
        : new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A0A4A8"));
}

// --- Connectors ---

public class AvailableIntegration
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string IconName { get; set; } = "";

    public string IconLetter => IntegrationIcons.GetLetter(Name, Type);
    public System.Windows.Media.Brush IconBackground => IntegrationIcons.GetBrush(Name, Type);
}

public class ConnectedIntegration
{
    public string Id { get; set; } = "";
    public string? Email { get; set; }
    public string App { get; set; } = "";
    public bool IsActive { get; set; }
    public string Access { get; set; } = "private";
    public int SyncedCount { get; set; }
    public string? AddedBy { get; set; }

    public string IconLetter => IntegrationIcons.GetLetter(DisplayName, App);
    public System.Windows.Media.Brush IconBackground => IntegrationIcons.GetBrush(DisplayName, App);

    public string StatusText => IsActive ? "Connected" : "Disconnected";
    public string DisplayName => App switch
    {
        "google" or "gmail" => "Google",
        "outlookMail" => "Outlook",
        "granola" => "Granola",
        "slack" => "Slack",
        "notion" => "Notion",
        _ => App
    };
}

// --- Knowledge Base ---

public class KnowledgeBaseFile
{
    public string Id { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string? Url { get; set; }
    public string Status { get; set; } = "";
    public long CreatedAt { get; set; }

    public string TypeIcon => SourceType switch
    {
        "pdf" => "PDF",
        "url" => "URL",
        "text" => "TXT",
        _ => "FILE"
    };

    public string FormattedDate
    {
        get
        {
            if (CreatedAt == 0) return "";
            var date = DateTimeOffset.FromUnixTimeSeconds(CreatedAt).LocalDateTime;
            return date.ToString("MMM d, yyyy");
        }
    }
}

// --- Instructions ---

public class AIInstruction
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public string Platforms { get; set; } = "";
    public long CreatedAt { get; set; }

    public string PlatformDisplay => Platforms switch
    {
        "whatsapp" => "WhatsApp",
        "gmail" => "Gmail",
        "linkedin" => "LinkedIn",
        "slack" => "Slack",
        "instagram" => "Instagram",
        _ => Platforms
    };
}

// --- Dictionary & Snippets ---

public class DictionaryWord
{
    public string Id { get; set; } = "";
    public string Word { get; set; } = "";
    public string Replacement { get; set; } = "";
    public string Source { get; set; } = "user";
    public string Type { get; set; } = "vocabulary";

    public string DisplayText => (Type == "vocabulary" || string.IsNullOrEmpty(Replacement) || Word == Replacement)
        ? Word
        : $"{Word} \u2192 {Replacement}";
}

// --- Prediction Logs ---

public class PredictionLogItem
{
    public string Id { get; set; } = "";
    public string Query { get; set; } = "";
    public string Response { get; set; } = "";
    public string Status { get; set; } = "";
    public long CreatedAt { get; set; }
    public string App { get; set; } = "";

    public string FormattedDate
    {
        get
        {
            if (CreatedAt == 0) return "";
            var date = DateTimeOffset.FromUnixTimeSeconds(CreatedAt).LocalDateTime;
            return date.ToString("MMM d, h:mm tt");
        }
    }
}

public class MemoryItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Content { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string Type { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long? FileSize { get; set; }
}

// Legacy compat — kept for any existing references
public class ConnectorInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsConnected { get; set; }
    public string? Icon { get; set; }
    public string? Description { get; set; }
    public DateTime? ConnectedAt { get; set; }
}

public class KnowledgeBase
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int DocumentCount { get; set; }
    public long TotalSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Instruction
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// --- Integration Icons ---

public static class IntegrationIcons
{
    private static System.Windows.Media.SolidColorBrush Brush(string hex) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    public static string GetLetter(string name, string type)
    {
        var key = (name + type).ToLowerInvariant();
        if (key.Contains("google") || key.Contains("gmail")) return "M";
        if (key.Contains("outlook")) return "O";
        if (key.Contains("granola")) return "G";
        if (key.Contains("notion")) return "N";
        if (key.Contains("slack")) return "S";
        if (key.Contains("hubspot")) return "H";
        if (name.Length > 0) return name[..1].ToUpper();
        return "?";
    }

    public static System.Windows.Media.Brush GetBrush(string name, string type)
    {
        var key = (name + type).ToLowerInvariant();
        if (key.Contains("google") || key.Contains("gmail"))
            return new System.Windows.Media.LinearGradientBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EA4335"),
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4285F4"), 45);
        if (key.Contains("outlook"))
            return Brush("#0078D4");
        if (key.Contains("granola"))
            return Brush("#4A6741");
        if (key.Contains("notion"))
            return Brush("#333333");
        if (key.Contains("slack"))
            return Brush("#4A154B");
        if (key.Contains("hubspot"))
            return Brush("#FF7A59");
        return Brush("#555");
    }
}

// VoiceLog and PredictionLog are defined in VoiceLogModels.cs
