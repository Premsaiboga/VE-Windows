using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VE.Windows.Theme;

namespace VE.Windows.Views.Onboarding;

public partial class OnboardingWindow : Window
{
    private int _currentStep;
    private readonly int _totalSteps = 6;
    private readonly Ellipse[] _dots;

    private readonly string[] _titles = {
        "Welcome to VE",
        "Accessibility Permission",
        "Microphone Permission",
        "AI Prediction",
        "Voice Dictation",
        "You're all set!"
    };

    private readonly string[] _descriptions = {
        "Your AI-powered desktop assistant for intelligent text prediction and voice dictation.",
        "VE needs keyboard access to detect when you hold modifier keys for prediction and dictation.",
        "VE needs microphone access for voice dictation and meeting notes.",
        "Hold Ctrl to get AI-powered text predictions based on your screen context. Text is automatically pasted.",
        "Hold the configured key to dictate text using your voice. Transcription is automatically pasted.",
        "VE is ready to use! It runs in your system tray and activates with keyboard shortcuts."
    };

    public OnboardingWindow()
    {
        InitializeComponent();

        _dots = new Ellipse[_totalSteps];
        for (int i = 0; i < _totalSteps; i++)
        {
            var dot = new Ellipse
            {
                Width = 8, Height = 8,
                Margin = new Thickness(4, 0, 4, 0),
                Fill = new SolidColorBrush(i == 0 ? ThemeManager.Instance.Blue : Color.FromRgb(85, 85, 85))
            };
            _dots[i] = dot;
            ProgressDots.Children.Add(dot);
        }

        ShowStep(0);
    }

    private void ShowStep(int step)
    {
        _currentStep = step;

        // Update dots
        for (int i = 0; i < _totalSteps; i++)
        {
            _dots[i].Fill = new SolidColorBrush(
                i == step ? ThemeManager.Instance.Blue :
                i < step ? ThemeManager.Instance.Green :
                Color.FromRgb(85, 85, 85));
        }

        // Update buttons
        BackButton.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = step == _totalSteps - 1 ? "Get Started" : "Next";

        // Update content
        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 400
        };

        panel.Children.Add(new TextBlock
        {
            Text = _titles[step],
            Foreground = ThemeManager.Instance.TextPrimaryBrush,
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(new TextBlock
        {
            Text = _descriptions[step],
            Foreground = ThemeManager.Instance.TextSecondaryBrush,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            LineHeight = 24,
            Margin = new Thickness(0, 0, 0, 24)
        });

        // Add step-specific content
        if (step == 3) // Prediction
        {
            var keyBadge = CreateKeyBadge("Ctrl");
            panel.Children.Add(keyBadge);
        }
        else if (step == 4) // Dictation
        {
            var keyBadge = CreateKeyBadge("Alt");
            panel.Children.Add(keyBadge);
        }

        StepContent.Content = panel;
    }

    private static Border CreateKeyBadge(string key)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24, 12, 24, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = key,
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas")
            }
        };
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 0) ShowStep(_currentStep - 1);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < _totalSteps - 1)
        {
            ShowStep(_currentStep + 1);
        }
        else
        {
            Managers.SettingsManager.Instance.Set("OnboardingCompleted", true);
            Close();
        }
    }
}
