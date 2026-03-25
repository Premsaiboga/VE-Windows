using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class ConnectorsView : UserControl
{
    private List<ConnectedIntegration> _connected = new();
    private List<AvailableIntegration> _available = new();

    public ConnectorsView()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadData();
    }

    private async Task LoadData()
    {
        LoadingText.Visibility = Visibility.Visible;
        ConnectedSection.Visibility = Visibility.Collapsed;
        AvailableSection.Visibility = Visibility.Collapsed;
        EmptyConnectedState.Visibility = Visibility.Collapsed;

        var connectedTask = ConnectorsService.Instance.GetConnectedIntegrations();
        var availableTask = ConnectorsService.Instance.GetAvailableIntegrations();
        await Task.WhenAll(connectedTask, availableTask);

        _connected = connectedTask.Result;
        _available = availableTask.Result;

        var connectedTypes = _connected.Where(c => c.IsActive).Select(c => c.App).ToHashSet();
        var filtered = _available.Where(a => !connectedTypes.Contains(a.Type)).ToList();

        DispatcherHelper.RunOnUI(() =>
        {
            LoadingText.Visibility = Visibility.Collapsed;

            if (_connected.Count > 0)
            {
                ConnectedList.ItemsSource = _connected;
                ConnectedSection.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyConnectedState.Visibility = Visibility.Visible;
            }

            if (filtered.Count > 0)
            {
                AvailableList.ItemsSource = filtered;
                AvailableSection.Visibility = Visibility.Visible;
            }
        });
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
        var connectedTypes = _connected.Where(c => c.IsActive).Select(c => c.App).ToHashSet();
        var filtered = _available.Where(a => !connectedTypes.Contains(a.Type)).ToList();

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Type.Contains(query, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        AvailableList.ItemsSource = filtered;
    }

    private async void ConnectIntegration_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is AvailableIntegration integration)
        {
            var redirectUrl = await ConnectorsService.Instance.ConnectIntegration(integration.Type);
            if (!string.IsNullOrEmpty(redirectUrl))
            {
                AppURLs.OpenUrl(redirectUrl);
            }
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ConnectedIntegration conn)
        {
            var result = MessageBox.Show(
                $"Disconnect {conn.DisplayName} ({conn.Email})?",
                "Disconnect Integration",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var success = await ConnectorsService.Instance.DisconnectIntegration(conn.App, conn.Id);
            if (success) await LoadData();
        }
    }
}
