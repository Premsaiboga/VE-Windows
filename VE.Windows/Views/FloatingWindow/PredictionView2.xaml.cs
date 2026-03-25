using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class PredictionView2 : UserControl
{
    private List<PredictionLogItem> _allLogs = new();

    public PredictionView2()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadLogs();
    }

    private async Task LoadLogs()
    {
        LoadingText.Visibility = Visibility.Visible;
        EmptyText.Visibility = Visibility.Collapsed;

        _allLogs = await VoiceService.Instance.GetPredictionLogs(1, 100);

        DispatcherHelper.RunOnUI(() =>
        {
            LoadingText.Visibility = Visibility.Collapsed;

            // Stats
            PredictionCountText.Text = _allLogs.Count.ToString();

            if (_allLogs.Count > 0)
            {
                RenderGroupedLogs(_allLogs);
            }
            else
            {
                EmptyText.Visibility = Visibility.Visible;
            }
        });
    }

    private void RenderGroupedLogs(List<PredictionLogItem> logs)
    {
        GroupedLogsList.Items.Clear();

        // Group by date
        var grouped = logs
            .OrderByDescending(l => l.CreatedAt)
            .GroupBy(l =>
            {
                if (l.CreatedAt == 0) return "Unknown";
                var date = DateTimeOffset.FromUnixTimeSeconds(l.CreatedAt).LocalDateTime;
                return FormatDateHeader(date);
            });

        foreach (var group in grouped)
        {
            // Date header
            var header = new TextBlock
            {
                Text = group.Key,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#878E92")),
                FontSize = 13,
                Margin = new Thickness(0, 16, 0, 8)
            };
            GroupedLogsList.Items.Add(header);

            // Items in group
            foreach (var item in group)
            {
                var row = CreateLogRow(item);
                GroupedLogsList.Items.Add(row);
            }
        }
    }

    private UIElement CreateLogRow(PredictionLogItem item)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // App icon
        var iconBorder = new Border
        {
            Width = 36, Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = GetAppIconBrush(item.App),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        var iconText = new TextBlock
        {
            Text = GetAppIconText(item.App),
            Foreground = Brushes.White,
            FontSize = 13, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = iconText;
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        // Content
        var content = new StackPanel();
        Grid.SetColumn(content, 1);

        // Query text
        var query = item.Query;
        var isTruncated = query.Length > 80;
        var queryBlock = new TextBlock
        {
            Text = isTruncated ? query[..80] + "..." : query,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4F5F5")),
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        };
        content.Children.Add(queryBlock);

        // "Click to see full" link for long texts
        if (isTruncated)
        {
            var seeFullBlock = new TextBlock
            {
                Text = "Click to see full",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00CA48")),
                FontSize = 11,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 2)
            };
            seeFullBlock.Tag = query;
            seeFullBlock.MouseLeftButtonDown += (s, e) =>
            {
                if (s is TextBlock tb)
                {
                    // Toggle between full and truncated
                    if (queryBlock.Text.EndsWith("..."))
                    {
                        queryBlock.Text = tb.Tag as string ?? "";
                        tb.Text = "Click to collapse";
                    }
                    else
                    {
                        queryBlock.Text = query[..80] + "...";
                        tb.Text = "Click to see full";
                    }
                }
            };
            content.Children.Add(seeFullBlock);
        }

        // Date + type label
        var dateStr = "";
        if (item.CreatedAt > 0)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(item.CreatedAt).LocalDateTime;
            dateStr = $"{date:d MMM} at {date:h:mm tt}";
        }
        var metaBlock = new TextBlock
        {
            Text = $"{dateStr}  \u00B7  Prediction",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666")),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0)
        };
        content.Children.Add(metaBlock);

        grid.Children.Add(content);
        return grid;
    }

    private static string FormatDateHeader(DateTime date)
    {
        var day = date.Day;
        var suffix = day switch
        {
            1 or 21 or 31 => "st",
            2 or 22 => "nd",
            3 or 23 => "rd",
            _ => "th"
        };
        return $"{day}{suffix} {date:MMM}".ToLower();
    }

    private static Brush GetAppIconBrush(string app)
    {
        var name = app.ToLowerInvariant();
        if (name.Contains("chrome") || name.Contains("google"))
        {
            return new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#EA4335"),
                (Color)ColorConverter.ConvertFromString("#4285F4"),
                45);
        }
        if (name.Contains("outlook") || name.Contains("microsoft"))
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
        if (name.Contains("slack"))
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A154B"));
        if (name.Contains("ve") || name.Contains("prediction"))
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007CEC"));

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555"));
    }

    private static string GetAppIconText(string app)
    {
        var name = app.ToLowerInvariant();
        if (name.Contains("chrome")) return "G";
        if (name.Contains("outlook")) return "O";
        if (name.Contains("slack")) return "S";
        if (name.Contains("ve")) return "ve";
        if (name.Length > 0) return name[..1].ToUpper();
        return "?";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(query))
        {
            RenderGroupedLogs(_allLogs);
            return;
        }

        var filtered = _allLogs.Where(l =>
            l.Query.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            l.Response.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            l.App.Contains(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        RenderGroupedLogs(filtered);
    }
}

// Simple converter: show element if string is non-empty
public class StringToVisConverter : IValueConverter
{
    public static readonly StringToVisConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
