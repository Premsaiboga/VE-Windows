using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VE.Windows.Managers;
using VE.Windows.Models;

namespace VE.Windows.ViewModels;

public class ChatViewModel : INotifyPropertyChanged
{
    private string _currentInput = "";
    private bool _isStreaming;
    private bool _isLoading;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChatMessage> Messages => ChatManager.Instance.Messages;

    public string CurrentInput
    {
        get => _currentInput;
        set { _currentInput = value; OnPropertyChanged(); }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set { _isStreaming = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool HasMessages => Messages.Count > 0;

    public async Task SendMessage()
    {
        var text = CurrentInput?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        CurrentInput = "";
        IsStreaming = true;
        OnPropertyChanged(nameof(HasMessages));

        await ChatManager.Instance.SendMessage(text);

        IsStreaming = false;
    }

    public void ClearHistory()
    {
        ChatManager.Instance.ClearChat();
        OnPropertyChanged(nameof(HasMessages));
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
