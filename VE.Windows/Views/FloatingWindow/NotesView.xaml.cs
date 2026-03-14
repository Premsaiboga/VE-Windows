using System.Windows;
using System.Windows.Controls;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class NotesView : UserControl
{
    public NotesView()
    {
        InitializeComponent();

        // Subscribe to meeting state changes
        MeetingService.Instance.PropertyChanged += (s, e) =>
        {
            Dispatcher.Invoke(UpdateMeetingUI);
        };

        // Subscribe to transcription changes
        MeetingService.Instance.LiveTranscriptions.CollectionChanged += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                TranscriptionList.ItemsSource = MeetingService.Instance.LiveTranscriptions;
                WelcomePanel.Visibility = MeetingService.Instance.LiveTranscriptions.Count > 0
                    ? Visibility.Collapsed : Visibility.Visible;

                // Auto-scroll
                if (TranscriptionScroll.ScrollableHeight > 0)
                {
                    TranscriptionScroll.ScrollToEnd();
                }
            });
        };
    }

    private void UpdateMeetingUI()
    {
        var isActive = MeetingService.Instance.IsActive;
        ActiveMeetingBanner.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;

        if (isActive)
        {
            var duration = MeetingService.Instance.Duration;
            MeetingDuration.Text = $"Recording {duration:mm\\:ss}";
            WelcomePanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void NewMeeting_Click(object sender, RoutedEventArgs e)
    {
        if (MeetingService.Instance.IsActive)
        {
            await MeetingService.Instance.StopMeeting();
        }
        else
        {
            await MeetingService.Instance.StartMeeting();
        }
    }

    private void PauseMeeting_Click(object sender, RoutedEventArgs e)
    {
        if (MeetingService.Instance.State == MeetingState.Active)
        {
            MeetingService.Instance.PauseMeeting();
        }
        else if (MeetingService.Instance.State == MeetingState.Paused)
        {
            MeetingService.Instance.ResumeMeeting();
        }
    }

    private async void StopMeeting_Click(object sender, RoutedEventArgs e)
    {
        await MeetingService.Instance.StopMeeting();
    }
}
