using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VE.Windows.Views.Components;

public partial class AudioWaveBars : UserControl
{
    public static readonly DependencyProperty BarColorProperty =
        DependencyProperty.Register(nameof(BarColor), typeof(Color), typeof(AudioWaveBars),
            new PropertyMetadata(Color.FromRgb(0, 124, 236)));

    public static readonly DependencyProperty BarWidthProperty =
        DependencyProperty.Register(nameof(BarWidth), typeof(double), typeof(AudioWaveBars),
            new PropertyMetadata(3.0));

    public static readonly DependencyProperty BarCountProperty =
        DependencyProperty.Register(nameof(BarCount), typeof(int), typeof(AudioWaveBars),
            new PropertyMetadata(5, OnBarCountChanged));

    public Color BarColor
    {
        get => (Color)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    public AudioWaveBars()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CreateBars();
    }

    private static void OnBarCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AudioWaveBars bars && bars.IsLoaded)
        {
            bars.CreateBars();
        }
    }

    private void CreateBars()
    {
        WaveCanvas.Children.Clear();

        var random = new Random(42); // Deterministic for consistent look
        var totalWidth = ActualWidth > 0 ? ActualWidth : Width;
        var totalHeight = ActualHeight > 0 ? ActualHeight : Height;
        var spacing = (totalWidth - BarCount * BarWidth) / (BarCount + 1);

        for (int i = 0; i < BarCount; i++)
        {
            var rect = new Rectangle
            {
                Width = BarWidth,
                Height = 4,
                Fill = new SolidColorBrush(BarColor),
                RadiusX = BarWidth / 2,
                RadiusY = BarWidth / 2,
                RenderTransformOrigin = new Point(0.5, 1)
            };

            Canvas.SetLeft(rect, spacing + i * (BarWidth + spacing));
            Canvas.SetBottom(rect, 0);

            // Animate height with different durations for organic feel
            var minHeight = 4.0;
            var maxHeight = totalHeight * (0.4 + random.NextDouble() * 0.6);
            var duration = TimeSpan.FromMilliseconds(300 + random.Next(400));
            var beginTime = TimeSpan.FromMilliseconds(i * 80);

            var animation = new DoubleAnimation
            {
                From = minHeight,
                To = maxHeight,
                Duration = new Duration(duration),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = beginTime,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            rect.BeginAnimation(HeightProperty, animation);
            WaveCanvas.Children.Add(rect);
        }
    }
}
