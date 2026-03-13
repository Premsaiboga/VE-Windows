using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VE.Windows.Views.Components;

public partial class SmoothDotLoader : UserControl
{
    public static readonly DependencyProperty DotColorProperty =
        DependencyProperty.Register(nameof(DotColor), typeof(Color), typeof(SmoothDotLoader),
            new PropertyMetadata(Colors.White, OnDotColorChanged));

    public static readonly DependencyProperty DotSizeProperty =
        DependencyProperty.Register(nameof(DotSize), typeof(double), typeof(SmoothDotLoader),
            new PropertyMetadata(6.0, OnDotSizeChanged));

    public Color DotColor
    {
        get => (Color)GetValue(DotColorProperty);
        set => SetValue(DotColorProperty, value);
    }

    public double DotSize
    {
        get => (double)GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    public SmoothDotLoader()
    {
        InitializeComponent();
    }

    private static void OnDotColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SmoothDotLoader loader)
        {
            var brush = new SolidColorBrush((Color)e.NewValue);
            loader.Dot1.Fill = brush;
            loader.Dot2.Fill = brush;
            loader.Dot3.Fill = brush;
        }
    }

    private static void OnDotSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SmoothDotLoader loader)
        {
            var size = (double)e.NewValue;
            loader.Dot1.Width = loader.Dot1.Height = size;
            loader.Dot2.Width = loader.Dot2.Height = size;
            loader.Dot3.Width = loader.Dot3.Height = size;
        }
    }
}
