using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.WebSocket;

namespace VE.Windows.Managers;

public sealed class ChatManager : INotifyPropertyChanged
{
    public static ChatManager Instance { get; } = new();

    private ChatConversation? _currentConversation;
    private bool _isStreaming;

    // Event handler references for cleanup
    private EventHandler<string>? _chunkHandler;
    private EventHandler<ChatResponse>? _completeHandler;
    private EventHandler<string>? _errorHandler;
    private EventHandler<List<Citation>>? _citationsHandler;
    private EventHandler<string>? _stepHandler;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ChatConversation? CurrentConversation
    {
        get => _currentConversation;
        set { _currentConversation = value; OnPropertyChanged(); }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        private set { _isStreaming = value; OnPropertyChanged(); }
    }

    private ChatManager() { }

    public async Task SendMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsStreaming) return;

        var userMessage = new ChatMessage
        {
            Role = ChatRole.User,
            Content = text
        };
        Messages.Add(userMessage);

        var assistantMessage = new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = "",
            IsStreaming = true
        };
        Messages.Add(assistantMessage);
        IsStreaming = true;

        try
        {
            // Always create a fresh connection for each conversation session (matches macOS)
            await WebSocketRegistry.Instance.ConnectMultiAgentTransport();
            var client = WebSocketRegistry.Instance.MultiAgentClient;

            // Wait for connection to establish (matches macOS 5s max wait)
            var maxWait = DateTime.UtcNow.AddSeconds(5);
            while (client != null && !client.IsConnected && DateTime.UtcNow < maxWait)
            {
                await Task.Delay(100);
            }

            if (client == null || !client.IsConnected)
            {
                assistantMessage.Content = "Failed to connect to AI service. Please check your connection.";
                assistantMessage.IsStreaming = false;
                IsStreaming = false;
                return;
            }

            // Cleanup old handlers
            CleanupHandlers(client);

            // Create new handlers
            _chunkHandler = (s, chunk) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    assistantMessage.Content += chunk;
                });
            };

            _stepHandler = (s, step) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (string.IsNullOrEmpty(assistantMessage.Content))
                    {
                        assistantMessage.ThinkingContent = step;
                    }
                });
            };

            _completeHandler = (s, response) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    assistantMessage.IsStreaming = false;
                    IsStreaming = false;
                    CleanupHandlers(client);
                });
            };

            _citationsHandler = (s, citations) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    assistantMessage.Citations = citations;
                });
            };

            _errorHandler = (s, error) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (string.IsNullOrEmpty(assistantMessage.Content))
                        assistantMessage.Content = $"Error: {error}";
                    assistantMessage.IsStreaming = false;
                    IsStreaming = false;
                    CleanupHandlers(client);
                });
            };

            client.OnResponseChunk += _chunkHandler;
            client.OnStepReceived += _stepHandler;
            client.OnResponseComplete += _completeHandler;
            client.OnCitationsReceived += _citationsHandler;
            client.OnError += _errorHandler;

            await client.SendChatMessage(text);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Chat", $"Send failed: {ex.Message}");
            assistantMessage.Content = $"Error: {ex.Message}";
            assistantMessage.IsStreaming = false;
            IsStreaming = false;
        }
    }

    private void CleanupHandlers(MultiAgentSocketClient client)
    {
        if (_chunkHandler != null) client.OnResponseChunk -= _chunkHandler;
        if (_stepHandler != null) client.OnStepReceived -= _stepHandler;
        if (_completeHandler != null) client.OnResponseComplete -= _completeHandler;
        if (_citationsHandler != null) client.OnCitationsReceived -= _citationsHandler;
        if (_errorHandler != null) client.OnError -= _errorHandler;
    }

    public void ClearChat()
    {
        Messages.Clear();
        CurrentConversation = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
