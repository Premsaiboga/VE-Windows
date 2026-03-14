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
