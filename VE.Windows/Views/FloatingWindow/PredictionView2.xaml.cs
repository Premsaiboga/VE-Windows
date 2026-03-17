using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VE.Windows.Helpers;
using VE.Windows.Services;

namespace VE.Windows.Views.FloatingWindow;

public partial class PredictionView2 : UserControl
{
    public PredictionView2()
    {
        InitializeComponent();
        Loaded += async (s, e) => await LoadLogs();
    }

    private async Task LoadLogs()
    {
        LoadingText.Visibility = Visibility.Visible;
        EmptyText.Visibility = Visibility.Collapsed;

        var logs = await VoiceService.Instance.GetPredictionLogs();

        DispatcherHelper.RunOnUI(() =>
        {
            LoadingText.Visibility = Visibility.Collapsed;

            if (logs.Count > 0)
            {
                LogsList.ItemsSource = logs;
            }
            else
            {
                EmptyText.Visibility = Visibility.Visible;
            }
        });
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
