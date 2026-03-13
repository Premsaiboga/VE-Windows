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
}

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
