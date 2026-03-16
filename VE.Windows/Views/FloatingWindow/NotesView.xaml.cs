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
    private List<TranscriptionItem> _allTranscriptions = new();
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

        // Inactivity alert
        MeetingService.Instance.InactivityAlertTriggered += (s, e) =>
        {
            DispatcherHelper.RunOnUI(() => InactivityOverlay.Visibility = Visibility.Visible);
        };

        // Summary generation complete via WebSocket
        MeetingService.Instance.SummaryGenerationComplete += (s, analytics) =>
        {
            DispatcherHelper.RunOnUI(() =>
            {
                GeneratingText.Visibility = Visibility.Collapsed;
                if (_selectedMeetingId != null)
                    _ = LoadSummary(_selectedMeetingId);
            });
        };

        MeetingService.Instance.SummaryGenerationSkipped += (s, e) =>
        {
            DispatcherHelper.RunOnUI(() =>
            {
                GeneratingText.Text = "Not enough transcription to generate summary.";
            });
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
            // Load upcoming calendar meetings and recent meetings in parallel
            var upcomingTask = CalendarService.Instance.GetUpcomingMeetings();
            var recentTask = MeetingGraphQLService.Instance.ListAllMeetings();
            await Task.WhenAll(upcomingTask, recentTask);

            var upcoming = upcomingTask.Result;
            var meetings = recentTask.Result;

            DispatcherHelper.RunOnUI(() =>
            {
                // Filter recent meetings: only show isTranscription == true (matches macOS)
                _allMeetings = meetings.Where(m => m.IsTranscription).ToList();

                // Update upcoming section
                if (upcoming.Count > 0)
                {
                    UpcomingList.ItemsSource = upcoming.Take(5).ToList(); // Show top 5
                    UpcomingSection.Visibility = Visibility.Visible;
                }
                else
                {
                    UpcomingSection.Visibility = Visibility.Collapsed;
                }

                UpdateMeetingsList();
                LoadingText.Visibility = Visibility.Collapsed;

                bool hasAny = _allMeetings.Count > 0 || upcoming.Count > 0;
                EmptyPanel.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
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
        GeneratingText.Text = "Writing Notes...";
        ActionItemsHeader.Visibility = Visibility.Collapsed;
        ActionItemsList.ItemsSource = null;
        DecisionsHeader.Visibility = Visibility.Collapsed;
        DecisionsList.ItemsSource = null;
        HighlightsHeader.Visibility = Visibility.Collapsed;
        HighlightsList.ItemsSource = null;

        // Meeting Prep
        MeetingPrepSection.Visibility = Visibility.Collapsed;
        MeetingPrepSummary.Visibility = Visibility.Collapsed;
        MeetingPrepSummary.Text = "";
        MeetingPrepList.ItemsSource = null;

        // Transcript
        TranscriptLoading.Text = "Loading transcript...";
        TranscriptLoading.Visibility = Visibility.Visible;
        TranscriptEmpty.Visibility = Visibility.Collapsed;
        TranscriptList.ItemsSource = null;
        TranscriptToolbar.Visibility = Visibility.Collapsed;
        TranscriptSearchBox.Text = "";
        _allTranscriptions = new();

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
            // Fetch meeting metadata, analytics, and prep in parallel
            var meetingTask = MeetingGraphQLService.Instance.GetMeetingSummary(meetingId);
            var analyticsTask = MeetingGraphQLService.Instance.GetMeetingAnalytics(meetingId);
            var prepTask = MeetingGraphQLService.Instance.GetMeetingPrep(meetingId);
            await Task.WhenAll(meetingTask, analyticsTask, prepTask);

            var summaryData = meetingTask.Result;
            var analyticsData = analyticsTask.Result;
            var prepData = prepTask.Result;

            DispatcherHelper.RunOnUI(() =>
            {
                SummaryLoading.Visibility = Visibility.Collapsed;
                SummaryData.Visibility = Visibility.Visible;

                // Meeting date info
                if (summaryData?.MeetingData != null)
                {
                    SummaryDate.Text = $"{summaryData.MeetingData.FormattedDate} at {summaryData.MeetingData.FormattedTime}";
                }

                // Meeting Prep: "Things you need to know" (shown before summary if available)
                if (prepData?.HasContent == true)
                {
                    MeetingPrepSection.Visibility = Visibility.Visible;
                    if (!string.IsNullOrEmpty(prepData.Summary))
                    {
                        MeetingPrepSummary.Text = prepData.Summary;
                        MeetingPrepSummary.Visibility = Visibility.Visible;
                    }
                    if (prepData.Items.Count > 0)
                    {
                        MeetingPrepList.ItemsSource = prepData.Items;
                    }
                }

                // Summary text comes from analytics (getMeeting's ListMeetingType doesn't have it)
                var summaryText = analyticsData?.Summary;
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
                    GenerateSummaryBtn.Visibility = Visibility.Visible;
                }

                // Action items from analytics
                if (analyticsData?.ActionItems.Count > 0)
                {
                    ActionItemsHeader.Visibility = Visibility.Visible;
                    ActionItemsList.ItemsSource = analyticsData.ActionItems;
                }

                // Decisions from analytics
                if (analyticsData?.Decisions.Count > 0)
                {
                    DecisionsHeader.Visibility = Visibility.Visible;
                    DecisionsList.ItemsSource = analyticsData.Decisions;
                }

                // Highlights from analytics
                if (analyticsData?.Highlights.Count > 0)
                {
                    HighlightsHeader.Visibility = Visibility.Visible;
                    HighlightsList.ItemsSource = analyticsData.Highlights;
                }
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
        GeneratingText.Text = "Writing Notes...";
        GeneratingText.Visibility = Visibility.Visible;

        // Use WebSocket-based streaming summary generation (matches macOS)
        var success = await MeetingService.Instance.TriggerSummaryGeneration(_selectedMeetingId);

        if (!success)
        {
            // Fallback to GraphQL mutation if WebSocket fails
            FileLogger.Instance.Warning("NotesView", "WebSocket summary failed, falling back to GraphQL");
            var gqlSuccess = await MeetingGraphQLService.Instance.GenerateMeetingSummary(_selectedMeetingId);
            if (gqlSuccess)
            {
                await Task.Delay(3000);
                await LoadSummary(_selectedMeetingId);
            }
            else
            {
                GeneratingText.Visibility = Visibility.Collapsed;
                GenerateSummaryBtn.Visibility = Visibility.Visible;
            }
        }
        // If WebSocket success, SummaryGenerationComplete event will reload the summary
    }

    // --- Transcript Tab ---

    private async Task LoadTranscriptions(string meetingId)
    {
        DispatcherHelper.RunOnUI(() =>
        {
            TranscriptLoading.Visibility = Visibility.Visible;
            TranscriptEmpty.Visibility = Visibility.Collapsed;
            TranscriptToolbar.Visibility = Visibility.Collapsed;
            TranscriptList.ItemsSource = null;
        });

        try
        {
            var transcriptions = await MeetingGraphQLService.Instance.ListTranscriptions(meetingId);

            DispatcherHelper.RunOnUI(() =>
            {
                TranscriptLoading.Visibility = Visibility.Collapsed;
                _allTranscriptions = transcriptions;

                if (transcriptions.Count > 0)
                {
                    TranscriptList.ItemsSource = transcriptions;
                    TranscriptToolbar.Visibility = Visibility.Visible;
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

    // --- Transcript Search & Copy ---

    private void TranscriptSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var query = TranscriptSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            TranscriptList.ItemsSource = _allTranscriptions;
            return;
        }

        var filtered = _allTranscriptions.Where(t =>
            t.Transcript.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            t.SpeakerName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        TranscriptList.ItemsSource = filtered;
    }

    private void CopyTranscript_Click(object sender, RoutedEventArgs e)
    {
        if (_allTranscriptions.Count == 0) return;

        var text = string.Join("\n\n", _allTranscriptions.Select(t =>
            $"[{t.FormattedTime}] {t.SpeakerName}: {t.Transcript}"));

        try
        {
            System.Windows.Clipboard.SetText(text);
            FileLogger.Instance.Info("NotesView", $"Copied {_allTranscriptions.Count} transcript entries to clipboard");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("NotesView", $"Copy to clipboard failed: {ex.Message}");
        }
    }

    // --- Delete Meeting ---

    private async void DeleteMeeting_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMeetingId == null) return;

        var result = System.Windows.MessageBox.Show(
            "Are you sure you want to delete this meeting?",
            "Delete Meeting",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var success = await MeetingGraphQLService.Instance.DeleteMeeting(_selectedMeetingId);
        if (success)
        {
            _selectedMeetingId = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            ListPanel.Visibility = Visibility.Visible;
            await LoadAllMeetings();
        }
    }

    // --- Inactivity Alert ---

    private void ContinueMeeting_Click(object sender, RoutedEventArgs e)
    {
        InactivityOverlay.Visibility = Visibility.Collapsed;
        MeetingService.Instance.ContinueFromInactivityAlert();
    }

    private async void EndMeetingFromAlert_Click(object sender, RoutedEventArgs e)
    {
        InactivityOverlay.Visibility = Visibility.Collapsed;
        await MeetingService.Instance.EndMeetingFromInactivityAlert();
    }
}

// Helper class for grouped meetings list
public class MeetingDateGroup
{
    public string DateHeader { get; set; } = "";
    public List<MeetingListItem> Meetings { get; set; } = new();
}
