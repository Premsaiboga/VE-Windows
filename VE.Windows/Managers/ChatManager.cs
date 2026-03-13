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
            var client = WebSocketRegistry.Instance.MultiAgentClient;
            if (client == null || !client.IsConnected)
            {
                await WebSocketRegistry.Instance.ConnectMultiAgentTransport();
                client = WebSocketRegistry.Instance.MultiAgentClient;
            }

            if (client == null)
            {
                assistantMessage.Content = "Failed to connect to AI service.";
                assistantMessage.IsStreaming = false;
                IsStreaming = false;
                return;
            }

            client.OnResponseChunk += (s, chunk) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    assistantMessage.Content += chunk;
                });
            };

            client.OnResponseComplete += (s, response) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (response.Citations != null)
                    {
                        assistantMessage.Citations = response.Citations;
                    }
                    assistantMessage.IsStreaming = false;
                    IsStreaming = false;
                });
            };

            client.OnError += (s, error) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    assistantMessage.Content = $"Error: {error}";
                    assistantMessage.IsStreaming = false;
                    IsStreaming = false;
                });
            };

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

    public void ClearChat()
    {
        Messages.Clear();
        CurrentConversation = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
