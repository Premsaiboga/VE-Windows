using System.Windows;
using System.Windows.Controls;
using VE.Windows.Services;

namespace VE.Windows.Views.Meeting;

public partial class MeetingView : UserControl
{
    public MeetingView()
    {
        InitializeComponent();

        MeetingService.Instance.PropertyChanged += (s, e) =>
        {
            Dispatcher.Invoke(UpdateState);
        };
    }

    private void UpdateState()
    {
        StartingPanel.Visibility = Visibility.Collapsed;
        ActivePanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        MeetingErrorPanel.Visibility = Visibility.Collapsed;

        var service = MeetingService.Instance;

        switch (service.State)
        {
            case MeetingState.Starting:
                StartingPanel.Visibility = Visibility.Visible;
                break;
            case MeetingState.Active:
                ActivePanel.Visibility = Visibility.Visible;
                MeetingTimer.Text = service.Duration.ToString(@"m\:ss");
                PauseButton.Content = "Pause";
                break;
            case MeetingState.Paused:
                ActivePanel.Visibility = Visibility.Visible;
                MeetingTimer.Text = service.Duration.ToString(@"m\:ss");
                PauseButton.Content = "Resume";
                break;
            case MeetingState.Result:
                ResultPanel.Visibility = Visibility.Visible;
                SummaryText.Text = "Meeting notes saved.";
                break;
            case MeetingState.Error:
                MeetingErrorPanel.Visibility = Visibility.Visible;
                break;
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (MeetingService.Instance.State == MeetingState.Active)
            MeetingService.Instance.PauseMeeting();
        else
            MeetingService.Instance.ResumeMeeting();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await MeetingService.Instance.StopMeeting();
    }

    private void ViewSummary_Click(object sender, RoutedEventArgs e)
    {
        // Open settings window to meeting notes
        ViewCoordinator.Instance.SelectedNavigationTab = NavigationTab.Notes;
    }

    private async void TryAgain_Click(object sender, RoutedEventArgs e)
    {
        await MeetingService.Instance.StartMeeting();
    }
}
