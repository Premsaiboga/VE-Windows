using System.Windows;
using System.Windows.Controls;
using VE.Windows.Managers;
using VE.Windows.Models;
using VE.Windows.Services;
using VE.Windows.Views.Settings;

namespace VE.Windows.Views.Notch;

public partial class NotchHomeView : UserControl
{
    /// <summary>
    /// Raised when the settings/floating panel button is clicked in the notch.
    /// MainWindow subscribes to this to show the floating panel.
    /// </summary>
    public static event EventHandler? OnFloatingPanelRequested;

    public NotchHomeView()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        AuthManager.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AuthManager.AuthState))
            {
                Dispatcher.Invoke(UpdateContent);
            }
        };

        ViewCoordinator.Instance.PropertyChanged += (s, e) =>
        {
            Dispatcher.Invoke(UpdateContent);
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateContent();
    }

    private void UpdateContent()
    {
        LoginContent.Visibility = Visibility.Collapsed;
        PredictionContent.Visibility = Visibility.Collapsed;
        DictationContent.Visibility = Visibility.Collapsed;
        MeetingContent.Visibility = Visibility.Collapsed;
        DefaultContent.Visibility = Visibility.Collapsed;

        if (AuthManager.Instance.AuthState != AuthState.Authorized)
        {
            LoginContent.Visibility = Visibility.Visible;
            return;
        }

        var coord = ViewCoordinator.Instance;

        if (coord.MeetingState != MeetingState.Inactive)
        {
            MeetingContent.Visibility = Visibility.Visible;
        }
        else if (coord.DictationState != DictationState.Inactive)
        {
            DictationContent.Visibility = Visibility.Visible;
        }
        else if (coord.CombinedPredictionState != CombinedPredictionState.Inactive)
        {
            PredictionContent.Visibility = Visibility.Visible;
        }
        else
        {
            DefaultContent.Visibility = Visibility.Visible;
        }
    }

    private static SettingsWindow? _settingsWindow;

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthManager.Instance.IsAuthenticated) return;

        // Open settings window directly
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow();
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
}
