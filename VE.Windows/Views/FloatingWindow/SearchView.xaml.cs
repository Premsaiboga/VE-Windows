using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VE.Windows.Helpers;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class SearchView : UserControl
{
    private readonly ObservableCollection<SearchResult> _results = new();
    private CancellationTokenSource? _searchCts;

    public SearchView()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _results;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = RunSearch();
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        _ = RunSearch();
    }

    private async Task RunSearch()
    {
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return;

        // Cancel any previous search
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        // Reset UI
        _results.Clear();
        NoResultsPanel.Visibility = Visibility.Collapsed;
        ResultCountText.Visibility = Visibility.Collapsed;
        SearchingPanel.Visibility = Visibility.Visible;
        SearchingText.Text = "Searching...";
        SearchingDetail.Text = "Checking Windows Search Index...";
        StatusText.Text = $"Searching for \"{query}\"...";

        try
        {
            var indexDone = false;

            var results = await LocalFileSearchService.Instance.SearchAsync(
                query, ct,
                onResultFound: result =>
                {
                    DispatcherHelper.RunOnUI(() =>
                    {
                        if (ct.IsCancellationRequested) return;

                        _results.Add(result);
                        ResultCountText.Text = $"{_results.Count} found";
                        ResultCountText.Visibility = Visibility.Visible;

                        // Hide searching panel once we have results
                        if (SearchingPanel.Visibility == Visibility.Visible)
                        {
                            SearchingPanel.Visibility = Visibility.Collapsed;
                        }

                        // Update detail text after index phase
                        if (!indexDone && _results.Count > 0)
                        {
                            indexDone = true;
                            SearchingDetail.Text = "Scanning file system for more...";
                        }
                    });
                },
                maxResults: 50);

            if (ct.IsCancellationRequested) return;

            DispatcherHelper.RunOnUI(() =>
            {
                SearchingPanel.Visibility = Visibility.Collapsed;

                if (_results.Count == 0)
                {
                    NoResultsPanel.Visibility = Visibility.Visible;
                    StatusText.Text = $"No results for \"{query}\"";
                }
                else
                {
                    StatusText.Text = $"Found {_results.Count} file{(_results.Count == 1 ? "" : "s")}";
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled by a new search — ignore
        }
        catch (Exception ex)
        {
            DispatcherHelper.RunOnUI(() =>
            {
                SearchingPanel.Visibility = Visibility.Collapsed;
                StatusText.Text = $"Search error: {ex.Message}";
                FileLogger.Instance.Error("SearchView", $"Search failed: {ex.Message}");
            });
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SearchResult result)
        {
            LocalFileSearchService.OpenFile(result.FullPath);
        }
    }
}
