using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class RecentChatsView : UserControl
{
    private List<RecentChatItem> _allChats = new();
    private int _currentPage = 1;
    private bool _hasMore;
    private bool _isLoading;

    public RecentChatsView()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (_allChats.Count == 0) await LoadChats(reset: true);
        };
    }

    private async Task LoadChats(bool reset = false)
    {
        if (_isLoading) return;
        _isLoading = true;

        if (reset)
        {
            _currentPage = 1;
            _allChats.Clear();
            DispatcherHelper.RunOnUI(() =>
            {
                LoadingText.Visibility = Visibility.Visible;
                EmptyText.Visibility = Visibility.Collapsed;
            });
        }
        else
        {
            DispatcherHelper.RunOnUI(() => LoadMoreText.Visibility = Visibility.Visible);
        }

        var (items, hasMore) = await ChatService.Instance.ListRecentSessions(_currentPage);
        _hasMore = hasMore;

        DispatcherHelper.RunOnUI(() =>
        {
            _allChats.AddRange(items);
            ChatsList.ItemsSource = null;
            ChatsList.ItemsSource = _allChats;

            LoadingText.Visibility = Visibility.Collapsed;
            LoadMoreText.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = _allChats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });

        _isLoading = false;
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 50 && _hasMore && !_isLoading)
        {
            _currentPage++;
            _ = LoadChats();
        }
    }

    private void Chat_Click(object sender, MouseButtonEventArgs e)
    {
        // Could navigate to chat detail - for now just log
        if (sender is FrameworkElement el && el.DataContext is RecentChatItem chat)
        {
            FileLogger.Instance.Info("RecentChats", $"Selected chat: {chat.Title}");
        }
    }

    private async void DeleteChat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is RecentChatItem chat)
        {
            var result = MessageBox.Show($"Delete \"{chat.Title}\"?", "Delete Chat",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var success = await ChatService.Instance.DeleteSession(chat.Id);
            if (success)
            {
                _allChats.Remove(chat);
                ChatsList.ItemsSource = null;
                ChatsList.ItemsSource = _allChats;
            }
        }
    }
}
