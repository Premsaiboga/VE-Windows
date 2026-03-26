using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class SharedWithMeView : UserControl
{
    private string _selectedTab = "Notes";
    private int _currentPage = 1;
    private const int PageLimit = 20;
    private bool _hasMorePages = true;
    private bool _isLoading;
    private bool _isDeleting;
    private bool _isLoadingMore;
    private List<MeetingListItem> _allMeetings = new();
    private List<MeetingListItem> _filteredMeetings = new();
    private readonly HashSet<string> _selectedIds = new();

    public SharedWithMeView()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadMeetings(reset: true);
    }

    // --- Data Loading ---

    private async Task LoadMeetings(bool reset = false)
    {
        if (_isLoading) return;
        _isLoading = true;

        if (reset)
        {
            _currentPage = 1;
            _hasMorePages = true;
            _allMeetings.Clear();
            _selectedIds.Clear();
            UpdateSelectionBar();
            ShimmerPanel.Visibility = Visibility.Visible;
        }

        try
        {
            var meetings = await MeetingGraphQLService.Instance.ListMeetings(
                _currentPage, PageLimit, "sharedWithMe");

            DispatcherHelper.RunOnUI(() =>
            {
                ShimmerPanel.Visibility = Visibility.Collapsed;

                if (meetings.Count < PageLimit)
                    _hasMorePages = false;

                // Deduplicate
                var existingIds = new HashSet<string>(_allMeetings.Select(m => m.Id));
                foreach (var m in meetings)
                {
                    if (!existingIds.Contains(m.Id) && m.IsTranscription)
                        _allMeetings.Add(m);
                }

                ApplyFilter();
                LoadMoreBtn.Visibility = _hasMorePages ? Visibility.Visible : Visibility.Collapsed;

                if (_allMeetings.Count == 0)
                    EmptyNotes.Visibility = Visibility.Visible;
                else
                    EmptyNotes.Visibility = Visibility.Collapsed;
            });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("SharedWithMe", $"LoadMeetings failed: {ex.Message}");
            DispatcherHelper.RunOnUI(() => ShimmerPanel.Visibility = Visibility.Collapsed);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var search = SearchBox.Text?.Trim() ?? "";
        _filteredMeetings = string.IsNullOrEmpty(search)
            ? _allMeetings.ToList()
            : _allMeetings.Where(m =>
                m.Title.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        RenderMeetingGroups();
    }

    private void RenderMeetingGroups()
    {
        MeetingGroupsList.Items.Clear();

        var grouped = _filteredMeetings
            .OrderByDescending(m => m.CreatedAt)
            .GroupBy(m =>
            {
                var date = m.CreatedDate.Date;
                if (date == DateTime.Today) return "Today";
                if (date == DateTime.Today.AddDays(-1)) return "Yesterday";
                if (date == DateTime.Today.AddDays(1)) return "Tomorrow";
                return FormatDateWithSuffix(date);
            });

        foreach (var group in grouped)
        {
            // Date header
            var header = new TextBlock
            {
                Text = group.Key,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = FindBrush("ThemeTextSecondary"),
                Margin = new Thickness(0, 8, 0, 6)
            };
            MeetingGroupsList.Items.Add(header);

            // Card for group
            var card = new Border
            {
                Background = FindBrush("ThemeCard"),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var cardPanel = new StackPanel();

            var meetingList = group.ToList();
            for (int i = 0; i < meetingList.Count; i++)
            {
                cardPanel.Children.Add(CreateMeetingRow(meetingList[i]));
                if (i < meetingList.Count - 1)
                {
                    cardPanel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = FindBrush("ThemeBorder"),
                        Opacity = 0.3,
                        Margin = new Thickness(12, 0, 12, 0)
                    });
                }
            }

            card.Child = cardPanel;
            MeetingGroupsList.Items.Add(card);
        }
    }

    private UIElement CreateMeetingRow(MeetingListItem meeting)
    {
        var row = new Grid
        {
            Margin = new Thickness(12, 10, 12, 10),
            Cursor = Cursors.Hand,
            Tag = meeting,
            Background = _selectedIds.Contains(meeting.Id)
                ? new SolidColorBrush(Color.FromArgb(26, 0, 124, 236))
                : Brushes.Transparent
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Checkbox (only visible when items are selected)
        var checkbox = new CheckBox
        {
            IsChecked = _selectedIds.Contains(meeting.Id),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Visibility = _selectedIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
            Tag = meeting
        };
        checkbox.Checked += (s, e) => ToggleSelection(meeting, true);
        checkbox.Unchecked += (s, e) => ToggleSelection(meeting, false);
        Grid.SetColumn(checkbox, 0);

        // Info
        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(new TextBlock
        {
            Text = meeting.Title,
            FontSize = 14, FontWeight = FontWeights.Medium,
            Foreground = FindBrush("ThemeTextPrimary"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 400
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = GetRelativeTime(meeting.CreatedDate),
            FontSize = 12, FontWeight = FontWeights.Medium,
            Foreground = FindBrush("ThemeTextSecondary")
        });
        Grid.SetColumn(infoPanel, 1);

        // 3-dot menu (visible on hover)
        var menuBtn = new TextBlock
        {
            Text = "\u22EF",
            FontSize = 18,
            Foreground = FindBrush("ThemeTextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Opacity = 0,
            Tag = meeting
        };
        menuBtn.MouseLeftButtonDown += MeetingMenu_Click;
        Grid.SetColumn(menuBtn, 2);

        row.Children.Add(checkbox);
        row.Children.Add(infoPanel);
        row.Children.Add(menuBtn);

        // Hover
        row.MouseEnter += (s, e) => menuBtn.Opacity = 1;
        row.MouseLeave += (s, e) => menuBtn.Opacity = 0;

        // Click
        row.MouseLeftButtonDown += (s, e) =>
        {
            if (e.OriginalSource is CheckBox) return;
            if (_selectedIds.Count > 0)
            {
                ToggleSelection(meeting, !_selectedIds.Contains(meeting.Id));
            }
            // else: would open meeting summary view
        };

        return row;
    }

    // --- Selection ---

    private void ToggleSelection(MeetingListItem meeting, bool selected)
    {
        if (selected)
            _selectedIds.Add(meeting.Id);
        else
            _selectedIds.Remove(meeting.Id);

        meeting.IsSelected = selected;
        UpdateSelectionBar();
        RenderMeetingGroups();
    }

    private void UpdateSelectionBar()
    {
        if (_selectedIds.Count > 0)
        {
            SelectionBar.Visibility = Visibility.Visible;
            SelectionCountText.Text = $"{_selectedIds.Count} selected";
        }
        else
        {
            SelectionBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearSelection_Click(object sender, MouseButtonEventArgs e)
    {
        _selectedIds.Clear();
        foreach (var m in _allMeetings) m.IsSelected = false;
        UpdateSelectionBar();
        RenderMeetingGroups();
    }

    // --- Bulk Delete ---

    private async void BulkDelete_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isDeleting || _selectedIds.Count == 0) return;

        _isDeleting = true;
        TrashButtonText.Text = "Deleting...";
        TrashSpinner.Visibility = Visibility.Visible;

        try
        {
            var idsToDelete = _selectedIds.ToList();
            var success = await MeetingGraphQLService.Instance.DeleteMeetingsByIds(idsToDelete);

            if (success)
            {
                _allMeetings.RemoveAll(m => idsToDelete.Contains(m.Id));
                _selectedIds.Clear();
                UpdateSelectionBar();

                // Reload from page 1
                await LoadMeetings(reset: true);
            }
        }
        finally
        {
            _isDeleting = false;
            TrashButtonText.Text = "Move to trash \U0001F5D1";
            TrashSpinner.Visibility = Visibility.Collapsed;
        }
    }

    // --- Single Delete (from menu) ---

    private async void MeetingMenu_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBlock tb || tb.Tag is not MeetingListItem meeting) return;

        // Simple context menu
        var menu = new ContextMenu();
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += async (s, args) =>
        {
            var success = await MeetingGraphQLService.Instance.DeleteMeeting(meeting.Id);
            if (success)
            {
                _allMeetings.RemoveAll(m => m.Id == meeting.Id);
                _selectedIds.Remove(meeting.Id);
                UpdateSelectionBar();
                ApplyFilter();
            }
        };
        menu.Items.Add(deleteItem);

        if (meeting.Status == "active" || meeting.Status == "recording")
        {
            var stopItem = new MenuItem { Header = "Stop" };
            menu.Items.Add(stopItem);
        }

        menu.IsOpen = true;
    }

    // --- Load More ---

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoadingMore || !_hasMorePages) return;

        _isLoadingMore = true;
        LoadMoreText.Text = "Loading...";
        LoadMoreSpinner.Visibility = Visibility.Visible;

        try
        {
            _currentPage++;
            await LoadMeetings();
        }
        finally
        {
            _isLoadingMore = false;
            LoadMoreText.Text = "Load more";
            LoadMoreSpinner.Visibility = Visibility.Collapsed;
        }
    }

    // --- Search ---

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    // --- Tab Switching ---

    private void DropdownPill_Click(object sender, MouseButtonEventArgs e)
    {
        DropdownMenu.Visibility = DropdownMenu.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        DropdownChevron.Text = DropdownMenu.Visibility == Visibility.Visible ? "\u25B4" : "\u25BE";
    }

    private void SelectTab(string tab)
    {
        _selectedTab = tab;
        DropdownLabel.Text = tab;
        DropdownMenu.Visibility = Visibility.Collapsed;
        DropdownChevron.Text = "\u25BE";

        NotesContent.Visibility = tab == "Notes" ? Visibility.Visible : Visibility.Collapsed;
        ChatsPlaceholder.Visibility = tab == "Chats" ? Visibility.Visible : Visibility.Collapsed;
        FilesPlaceholder.Visibility = tab == "Files" ? Visibility.Visible : Visibility.Collapsed;
        SearchBarBorder.Visibility = tab == "Notes" ? Visibility.Visible : Visibility.Collapsed;
        SelectionBar.Visibility = Visibility.Collapsed;

        // Update search placeholder
        SearchPlaceholder.Text = $"Search {tab}";
    }

    private void DropdownItem_Notes(object sender, MouseButtonEventArgs e) => SelectTab("Notes");
    private void DropdownItem_Chats(object sender, MouseButtonEventArgs e) => SelectTab("Chats");
    private void DropdownItem_Files(object sender, MouseButtonEventArgs e) => SelectTab("Files");

    // --- Helpers ---

    private static string FormatDateWithSuffix(DateTime date)
    {
        var day = date.Day;
        var suffix = day switch
        {
            1 or 21 or 31 => "st",
            2 or 22 => "nd",
            3 or 23 => "rd",
            _ => "th"
        };
        return $"{date:MMMM} {day}{suffix}";
    }

    private static string GetRelativeTime(DateTime date)
    {
        var diff = DateTime.Now - date;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        return date.ToString("MMM d");
    }

    private static Brush FindBrush(string key)
    {
        return Application.Current.Resources[key] as Brush
               ?? new SolidColorBrush(Colors.Gray);
    }
}
