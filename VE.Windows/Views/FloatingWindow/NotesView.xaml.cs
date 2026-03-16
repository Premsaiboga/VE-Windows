using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

/// <summary>
/// Meeting Notes view — shows meetings list with grouped dates,
/// and a detail view with Summary/Transcript/Analytics tabs.
/// Matches macOS MeetingsListView + MeetingSummaryView.
/// </summary>
public partial class NotesView : UserControl
{
    private List<MeetingListItem> _allMeetings = new();
    private bool _isLoading;
    private string? _selectedMeetingId;
    private string _currentDetailTab = "Summary";

    public NotesView()
    {
        InitializeComponent();

        // Subscribe to meeting state changes (for active recording banner)
        MeetingService.Instance.PropertyChanged += (s, e) =>
        {
            DispatcherHelper.RunOnUI(UpdateMeetingBanner);
        };

        // Refresh list when a meeting ends
        MeetingService.Instance.MeetingListNeedsRefresh += (s, e) =>
        {
            _ = LoadAllMeetings();
        };

        // Load meetings on first show
        Loaded += async (s, e) =>
        {
            if (_allMeetings.Count == 0)
                await LoadAllMeetings();
        };
    }

    // --- Meetings List ---

    private async Task LoadAllMeetings()
    {
        if (_isLoading) return;
        _isLoading = true;

        DispatcherHelper.RunOnUI(() =>
        {
            LoadingText.Visibility = Visibility.Visible;
            EmptyPanel.Visibility = Visibility.Collapsed;
        });

        try
        {
            var meetings = await MeetingGraphQLService.Instance.ListAllMeetings();

            DispatcherHelper.RunOnUI(() =>
            {
                _allMeetings = meetings;
                UpdateMeetingsList();
                LoadingText.Visibility = Visibility.Collapsed;
                EmptyPanel.Visibility = _allMeetings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("NotesView", $"LoadAllMeetings failed: {ex.Message}");
            DispatcherHelper.RunOnUI(() =>
            {
                LoadingText.Visibility = Visibility.Collapsed;
            });
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void UpdateMeetingsList()
    {
        var grouped = _allMeetings
            .GroupBy(m => m.FormattedDate)
            .OrderByDescending(g => g.First().CreatedAt)
            .Select(g => new MeetingDateGroup
            {
                DateHeader = g.Key,
                Meetings = g.OrderByDescending(m => m.CreatedAt).ToList()
            })
            .ToList();

        MeetingsList.ItemsSource = grouped;
    }

    // --- Navigation ---

    private void Meeting_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MeetingListItem meeting)
        {
            ShowMeetingDetail(meeting.Id, meeting.Title);
        }
    }

    private void ShowMeetingDetail(string meetingId, string title)
    {
        _selectedMeetingId = meetingId;
        _currentDetailTab = "Summary";

        DetailTitle.Text = title;
        ListPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;

        // Reset all detail UI to loading state
        ResetDetailUI();
        UpdateDetailTabSelection();
        ShowDetailTab("Summary");
    }

    private void ResetDetailUI()
    {
        // Summary
        SummaryLoading.Text = "Loading summary...";
        SummaryLoading.Visibility = Visibility.Visible;
        SummaryData.Visibility = Visibility.Collapsed;
        SummaryText.Text = "";
        SummaryDate.Text = "";
        GenerateSummaryBtn.Visibility = Visibility.Collapsed;
        GeneratingText.Visibility = Visibility.Collapsed;
        ActionItemsHeader.Visibility = Visibility.Collapsed;
        ActionItemsList.ItemsSource = null;
        DecisionsHeader.Visibility = Visibility.Collapsed;
        DecisionsList.ItemsSource = null;
        HighlightsHeader.Visibility = Visibility.Collapsed;
        HighlightsList.ItemsSource = null;

        // Transcript
        TranscriptLoading.Text = "Loading transcript...";
        TranscriptLoading.Visibility = Visibility.Visible;
        TranscriptEmpty.Visibility = Visibility.Collapsed;
        TranscriptList.ItemsSource = null;

        // Analytics
        AnalyticsLoading.Text = "Loading analytics...";
        AnalyticsLoading.Visibility = Visibility.Visible;
        AnalyticsData.Visibility = Visibility.Collapsed;
        AnalyticsNotGenerated.Visibility = Visibility.Collapsed;
        ChaptersHeader.Visibility = Visibility.Collapsed;
        ChaptersList.ItemsSource = null;
        QuestionsHeader.Visibility = Visibility.Collapsed;
        QuestionsList.ItemsSource = null;
        ParticipantsHeader.Visibility = Visibility.Collapsed;
        ParticipantsList.ItemsSource = null;
    }

    private void BackToList_Click(object sender, RoutedEventArgs e)
    {
        _selectedMeetingId = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        ListPanel.Visibility = Visibility.Visible;
    }

    // --- Detail Tabs ---

    private void DetailTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tab)
        {
            _currentDetailTab = tab;
            UpdateDetailTabSelection();
            ShowDetailTab(tab);
        }
    }

    private void UpdateDetailTabSelection()
    {
        var tabs = new[] { TabSummary, TabTranscript, TabAnalytics };
        foreach (var tab in tabs)
        {
            var isSelected = (tab.Tag as string) == _currentDetailTab;
            tab.Foreground = isSelected
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 124, 236))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(135, 142, 146));
            tab.BorderBrush = isSelected
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 124, 236))
                : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void ShowDetailTab(string tab)
    {
        SummaryContent.Visibility = tab == "Summary" ? Visibility.Visible : Visibility.Collapsed;
        TranscriptContent.Visibility = tab == "Transcript" ? Visibility.Visible : Visibility.Collapsed;
        AnalyticsContent.Visibility = tab == "Analytics" ? Visibility.Visible : Visibility.Collapsed;

        if (_selectedMeetingId == null) return;

        // Always fetch fresh data for the selected meeting
        switch (tab)
        {
            case "Summary":
                _ = LoadSummary(_selectedMeetingId);
                break;
            case "Transcript":
                _ = LoadTranscriptions(_selectedMeetingId);
                break;
            case "Analytics":
                _ = LoadAnalytics(_selectedMeetingId);
                break;
        }
    }

    // --- Summary Tab ---

    private async Task LoadSummary(string meetingId)
    {
        DispatcherHelper.RunOnUI(() =>
        {
            SummaryLoading.Visibility = Visibility.Visible;
            SummaryData.Visibility = Visibility.Collapsed;
        });

        try
        {
            var summaryData = await MeetingGraphQLService.Instance.GetMeetingSummary(meetingId);

            DispatcherHelper.RunOnUI(() =>
            {
                SummaryLoading.Visibility = Visibility.Collapsed;
                SummaryData.Visibility = Visibility.Visible;

                // Meeting date info
                if (summaryData?.MeetingData != null)
                {
                    SummaryDate.Text = $"{summaryData.MeetingData.FormattedDate} at {summaryData.MeetingData.FormattedTime}";
                }

                // Summary text
                var summaryText = summaryData?.TranscriptionSummary;
                if (!string.IsNullOrEmpty(summaryText))
                {
                    SummaryText.Text = summaryText;
                    SummaryText.Visibility = Visibility.Visible;
                    GenerateSummaryBtn.Visibility = Visibility.Collapsed;
                }
                else
                {
                    SummaryText.Text = "";
                    SummaryText.Visibility = Visibility.Collapsed;
                    // Show Generate Summary button when no summary exists
                    GenerateSummaryBtn.Visibility = Visibility.Visible;
                }

                // Don't pre-fetch analytics here — let each tab fetch its own data
            });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("NotesView", $"LoadSummary failed: {ex.Message}");
            DispatcherHelper.RunOnUI(() =>
            {
                SummaryLoading.Visibility = Visibility.Collapsed;
                SummaryData.Visibility = Visibility.Visible;
                SummaryText.Text = "Failed to load summary.";
                SummaryText.Visibility = Visibility.Visible;
                GenerateSummaryBtn.Visibility = Visibility.Visible;
            });
        }
    }

    // --- Generate Summary ---

    private async void GenerateSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMeetingId == null) return;

        GenerateSummaryBtn.Visibility = Visibility.Collapsed;
        GeneratingText.Visibility = Visibility.Visible;

        var success = await MeetingGraphQLService.Instance.GenerateMeetingSummary(_selectedMeetingId);

        if (success)
        {
            // Wait a bit for server to process, then reload
            await Task.Delay(3000);
            await LoadSummary(_selectedMeetingId);
        }
        else
        {
            GeneratingText.Visibility = Visibility.Collapsed;
            GenerateSummaryBtn.Visibility = Visibility.Visible;
        }
    }

    // --- Transcript Tab ---

    private async Task LoadTranscriptions(string meetingId)
    {
        DispatcherHelper.RunOnUI(() =>
        {
            TranscriptLoading.Visibility = Visibility.Visible;
            TranscriptEmpty.Visibility = Visibility.Collapsed;
            TranscriptList.ItemsSource = null;
        });

        try
        {
            var transcriptions = await MeetingGraphQLService.Instance.ListTranscriptions(meetingId);

            DispatcherHelper.RunOnUI(() =>
            {
                TranscriptLoading.Visibility = Visibility.Collapsed;

                if (transcriptions.Count > 0)
                {
                    TranscriptList.ItemsSource = transcriptions;
                    TranscriptEmpty.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TranscriptEmpty.Visibility = Visibility.Visible;
                }
            });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("NotesView", $"LoadTranscriptions failed: {ex.Message}");
            DispatcherHelper.RunOnUI(() =>
            {
                TranscriptLoading.Text = "Failed to load transcript.";
            });
        }
    }

    // --- Analytics Tab ---

    private async Task LoadAnalytics(string meetingId)
    {
        DispatcherHelper.RunOnUI(() =>
        {
            AnalyticsLoading.Visibility = Visibility.Visible;
            AnalyticsData.Visibility = Visibility.Collapsed;
        });

        try
        {
            var analyticsData = await MeetingGraphQLService.Instance.GetMeetingAnalytics(meetingId);

            DispatcherHelper.RunOnUI(() =>
            {
                AnalyticsLoading.Visibility = Visibility.Collapsed;
                AnalyticsData.Visibility = Visibility.Visible;

                // Determine if analytics actually has data (don't rely solely on AnalyticsGenerated flag)
                bool hasData = analyticsData != null &&
                    (analyticsData.Chapters.Count > 0 ||
                     analyticsData.Highlights.Count > 0 ||
                     analyticsData.ActionItems.Count > 0 ||
                     analyticsData.Participants.Count > 0 ||
                     !string.IsNullOrEmpty(analyticsData.Summary));

                if (!hasData)
                {
                    AnalyticsNotGenerated.Visibility = Visibility.Visible;
                    ChaptersHeader.Visibility = Visibility.Collapsed;
                    ChaptersList.ItemsSource = null;
                    QuestionsHeader.Visibility = Visibility.Collapsed;
                    QuestionsList.ItemsSource = null;
                    ParticipantsHeader.Visibility = Visibility.Collapsed;
                    ParticipantsList.ItemsSource = null;
                    return;
                }

                AnalyticsNotGenerated.Visibility = Visibility.Collapsed;

                // Also populate summary action items/decisions/highlights if we have them
                if (analyticsData!.ActionItems.Count > 0)
                {
                    ActionItemsHeader.Visibility = Visibility.Visible;
                    ActionItemsList.ItemsSource = analyticsData.ActionItems;
                }

                if (analyticsData.Decisions.Count > 0)
                {
                    DecisionsHeader.Visibility = Visibility.Visible;
                    DecisionsList.ItemsSource = analyticsData.Decisions;
                }

                if (analyticsData.Highlights.Count > 0)
                {
                    HighlightsHeader.Visibility = Visibility.Visible;
                    HighlightsList.ItemsSource = analyticsData.Highlights;
                }

                // Chapters
                if (analyticsData.Chapters.Count > 0)
                {
                    ChaptersHeader.Visibility = Visibility.Visible;
                    ChaptersList.ItemsSource = analyticsData.Chapters;
                }
                else
                {
                    ChaptersHeader.Visibility = Visibility.Collapsed;
                    ChaptersList.ItemsSource = null;
                }

                // Open Questions
                if (analyticsData.OpenQuestions.Count > 0)
                {
                    QuestionsHeader.Visibility = Visibility.Visible;
                    QuestionsList.ItemsSource = analyticsData.OpenQuestions;
                }
                else
                {
                    QuestionsHeader.Visibility = Visibility.Collapsed;
                    QuestionsList.ItemsSource = null;
                }

                // Participants
                if (analyticsData.Participants.Count > 0)
                {
                    ParticipantsHeader.Visibility = Visibility.Visible;
                    ParticipantsList.ItemsSource = analyticsData.Participants;
                }
                else
                {
                    ParticipantsHeader.Visibility = Visibility.Collapsed;
                    ParticipantsList.ItemsSource = null;
                }
            });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("NotesView", $"LoadAnalytics failed: {ex.Message}");
            DispatcherHelper.RunOnUI(() =>
            {
                AnalyticsLoading.Visibility = Visibility.Collapsed;
                AnalyticsData.Visibility = Visibility.Visible;
                AnalyticsNotGenerated.Visibility = Visibility.Visible;
            });
        }
    }

    // --- Active Meeting Banner ---

    private void UpdateMeetingBanner()
    {
        var isActive = MeetingService.Instance.IsActive;
        ActiveMeetingBanner.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;

        if (isActive)
        {
            var duration = MeetingService.Instance.Duration;
            MeetingDuration.Text = $"Recording {duration:mm\\:ss}";
        }
    }

    // --- Button Handlers ---

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
            MeetingService.Instance.PauseMeeting();
        else if (MeetingService.Instance.State == MeetingState.Paused)
            MeetingService.Instance.ResumeMeeting();
    }

    private async void StopMeeting_Click(object sender, RoutedEventArgs e)
    {
        await MeetingService.Instance.StopMeeting();
    }
}

// Helper class for grouped meetings list
public class MeetingDateGroup
{
    public string DateHeader { get; set; } = "";
    public List<MeetingListItem> Meetings { get; set; } = new();
}
