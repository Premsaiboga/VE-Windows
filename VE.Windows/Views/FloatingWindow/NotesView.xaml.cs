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

    // Cached detail data
    private MeetingSummaryData? _summaryData;
    private MeetingAnalyticsData? _analyticsData;
    private List<TranscriptionItem>? _transcriptions;

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
                // Show all meetings (macOS filters to IsTranscription but we show all)
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
        // Group meetings by date (matches macOS groupedMeetings)
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
        _summaryData = null;
        _analyticsData = null;
        _transcriptions = null;
        _currentDetailTab = "Summary";

        DetailTitle.Text = title;
        ListPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;

        UpdateDetailTabSelection();
        ShowDetailTab("Summary");

        // Load summary data
        _ = LoadSummary(meetingId);
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

        switch (tab)
        {
            case "Summary" when _summaryData == null:
                _ = LoadSummary(_selectedMeetingId);
                break;
            case "Transcript" when _transcriptions == null:
                _ = LoadTranscriptions(_selectedMeetingId);
                break;
            case "Analytics" when _analyticsData == null:
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
            _summaryData = await MeetingGraphQLService.Instance.GetMeetingSummary(meetingId);

            // Also load analytics for summary section (action items, decisions, highlights)
            _analyticsData ??= await MeetingGraphQLService.Instance.GetMeetingAnalytics(meetingId);

            DispatcherHelper.RunOnUI(() =>
            {
                SummaryLoading.Visibility = Visibility.Collapsed;
                SummaryData.Visibility = Visibility.Visible;

                // Meeting date info
                if (_summaryData?.MeetingData != null)
                {
                    SummaryDate.Text = $"{_summaryData.MeetingData.FormattedDate} at {_summaryData.MeetingData.FormattedTime}";
                }

                // Summary text — prefer analytics summary, fallback to transcriptionSummary
                var summaryText = _analyticsData?.Summary ?? _summaryData?.TranscriptionSummary;
                if (!string.IsNullOrEmpty(summaryText))
                {
                    SummaryText.Text = summaryText;
                    SummaryText.Visibility = Visibility.Visible;
                }
                else
                {
                    SummaryText.Text = "No summary available. The meeting may still be processing.";
                    SummaryText.Visibility = Visibility.Visible;
                }

                // Action items
                if (_analyticsData?.ActionItems.Count > 0)
                {
                    ActionItemsHeader.Visibility = Visibility.Visible;
                    ActionItemsList.ItemsSource = _analyticsData.ActionItems;
                }
                else
                {
                    ActionItemsHeader.Visibility = Visibility.Collapsed;
                    ActionItemsList.ItemsSource = null;
                }

                // Decisions
                if (_analyticsData?.Decisions.Count > 0)
                {
                    DecisionsHeader.Visibility = Visibility.Visible;
                    DecisionsList.ItemsSource = _analyticsData.Decisions;
                }
                else
                {
                    DecisionsHeader.Visibility = Visibility.Collapsed;
                    DecisionsList.ItemsSource = null;
                }

                // Highlights
                if (_analyticsData?.Highlights.Count > 0)
                {
                    HighlightsHeader.Visibility = Visibility.Visible;
                    HighlightsList.ItemsSource = _analyticsData.Highlights;
                }
                else
                {
                    HighlightsHeader.Visibility = Visibility.Collapsed;
                    HighlightsList.ItemsSource = null;
                }
            });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("NotesView", $"LoadSummary failed: {ex.Message}");
            DispatcherHelper.RunOnUI(() =>
            {
                SummaryLoading.Text = "Failed to load summary.";
            });
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
            _transcriptions = await MeetingGraphQLService.Instance.ListTranscriptions(meetingId);

            DispatcherHelper.RunOnUI(() =>
            {
                TranscriptLoading.Visibility = Visibility.Collapsed;

                if (_transcriptions.Count > 0)
                {
                    TranscriptList.ItemsSource = _transcriptions;
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
            _analyticsData ??= await MeetingGraphQLService.Instance.GetMeetingAnalytics(meetingId);

            DispatcherHelper.RunOnUI(() =>
            {
                AnalyticsLoading.Visibility = Visibility.Collapsed;
                AnalyticsData.Visibility = Visibility.Visible;

                if (_analyticsData == null || !_analyticsData.AnalyticsGenerated)
                {
                    AnalyticsNotGenerated.Visibility = Visibility.Visible;
                    ChaptersHeader.Visibility = Visibility.Collapsed;
                    QuestionsHeader.Visibility = Visibility.Collapsed;
                    ParticipantsHeader.Visibility = Visibility.Collapsed;
                    return;
                }

                AnalyticsNotGenerated.Visibility = Visibility.Collapsed;

                // Chapters
                if (_analyticsData.Chapters.Count > 0)
                {
                    ChaptersHeader.Visibility = Visibility.Visible;
                    ChaptersList.ItemsSource = _analyticsData.Chapters;
                }
                else
                {
                    ChaptersHeader.Visibility = Visibility.Collapsed;
                    ChaptersList.ItemsSource = null;
                }

                // Open Questions
                if (_analyticsData.OpenQuestions.Count > 0)
                {
                    QuestionsHeader.Visibility = Visibility.Visible;
                    QuestionsList.ItemsSource = _analyticsData.OpenQuestions;
                }
                else
                {
                    QuestionsHeader.Visibility = Visibility.Collapsed;
                    QuestionsList.ItemsSource = null;
                }

                // Participants
                if (_analyticsData.Participants.Count > 0)
                {
                    ParticipantsHeader.Visibility = Visibility.Visible;
                    ParticipantsList.ItemsSource = _analyticsData.Participants;
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
                AnalyticsLoading.Text = "Failed to load analytics.";
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
