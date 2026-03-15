using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VE.Windows.Managers;
using VE.Windows.ViewModels;

namespace VE.Windows.Views.FloatingWindow;

public partial class ChatView : UserControl
{
    private readonly ChatViewModel _vm = new();

    public ChatView()
    {
        InitializeComponent();
        DataContext = _vm;

        // Set ItemsSource ONCE - ObservableCollection handles updates automatically
        MessagesList.ItemsSource = _vm.Messages;

        _vm.Messages.CollectionChanged += (s, e) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                WelcomePanel.Visibility = _vm.HasMessages ? Visibility.Collapsed : Visibility.Visible;

                // Auto-scroll to bottom
                if (MessagesScroll.ScrollableHeight > 0)
                {
                    MessagesScroll.ScrollToEnd();
                }
            });
        };
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessage();
    }

    private async void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        var text = ChatInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _vm.CurrentInput = text;
        ChatInput.Text = "";
        await _vm.SendMessage();
    }

    private void ClearChat_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearHistory();
        WelcomePanel.Visibility = Visibility.Visible;
    }
}
