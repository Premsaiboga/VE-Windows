using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VE.Windows.Models;

public enum ChatRole
{
    User,
    Assistant,
    System
}

public class ChatMessage : INotifyPropertyChanged
{
    private string _content = "";
    private bool _isStreaming;
    private string? _thinkingContent;
    private List<Citation> _citations = new();
    private List<ThinkingStep> _thinkingSteps = new();
    private List<ChatAttachment> _attachments = new();
    private bool _isCancelled;
    private bool _isThinkingExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ChatRole Role { get; set; }

    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public List<Citation> Citations
    {
        get => _citations;
        set { _citations = value; OnPropertyChanged(); }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set { _isStreaming = value; OnPropertyChanged(); }
    }

    public string? ThinkingContent
    {
        get => _thinkingContent;
        set { _thinkingContent = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Structured thinking steps from the AI model. Matches macOS ThinkingStep array.
    /// </summary>
    public List<ThinkingStep> ThinkingSteps
    {
        get => _thinkingSteps;
        set { _thinkingSteps = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// File/image attachments. Matches macOS ChatAttachment array.
    /// </summary>
    public List<ChatAttachment> Attachments
    {
        get => _attachments;
        set { _attachments = value; OnPropertyChanged(); }
    }

    public bool IsCancelled
    {
        get => _isCancelled;
        set { _isCancelled = value; OnPropertyChanged(); }
    }

    public bool IsThinkingExpanded
    {
        get => _isThinkingExpanded;
        set { _isThinkingExpanded = value; OnPropertyChanged(); }
    }

    public List<CodeBlock> CodeBlocks { get; set; } = new();

    // Helper properties for XAML binding
    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ChatConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Citation
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string? Favicon { get; set; }
}

public class CodeBlock
{
    public string Language { get; set; } = "";
    public string Code { get; set; } = "";
    public string? FileName { get; set; }
}

public class ChatResponse
{
    public string? Id { get; set; }
    public string? Text { get; set; }
    public bool IsComplete { get; set; }
    public List<Citation>? Citations { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// AI model selection for chat. Matches macOS AIModel enum.
/// "auto" means let the backend choose the best model.
/// </summary>
public class AIModel
{
    public string Id { get; init; } = "auto";
    public string DisplayName { get; init; } = "Ve (Auto)";

    public static AIModel Auto => new() { Id = "auto", DisplayName = "Ve (Auto)" };

    public static readonly AIModel[] AvailableModels =
    {
        Auto,
        new() { Id = "gpt-4o", DisplayName = "GPT-4o" },
        new() { Id = "gpt-4o-mini", DisplayName = "GPT-4o Mini" },
        new() { Id = "kimi-k2", DisplayName = "Kimi K2" },
        new() { Id = "o4-mini", DisplayName = "O4 Mini" },
    };
}

/// <summary>
/// Thinking step during chat stream. Matches macOS ThinkingStep.
/// </summary>
public class ThinkingStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = "";
    public string? ToolName { get; set; }
    public List<string>? Queries { get; set; }
    public List<Citation>? Sources { get; set; }
    public bool IsComplete { get; set; }
}

/// <summary>
/// Chat attachment for file/image uploads. Matches macOS ChatAttachment.
/// </summary>
public class ChatAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FileName { get; set; } = "";
    public string FileType { get; set; } = "";
    public long FileSize { get; set; }
    public double UploadProgress { get; set; }
    public string? S3Url { get; set; }
    public string? Base64Data { get; set; }
    public bool IsImage { get; set; }
    public bool IsUploaded { get; set; }
    public AttachmentStatus Status { get; set; } = AttachmentStatus.Pending;
}

public enum AttachmentStatus
{
    Pending,
    Uploading,
    Uploaded,
    Failed
}

/// <summary>
/// Recent chat session for sidebar display. Matches macOS RecentChat.
/// </summary>
public class RecentChatItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string LastMessage { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int MessageCount { get; set; }
}
