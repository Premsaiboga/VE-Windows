using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace VE.Windows.Views.Notch;

public partial class ClosedNotchView : UserControl
{
    public ClosedNotchView()
    {
        InitializeComponent();
    }

    private void HideAll()
    {
        IdleContent.Visibility = Visibility.Collapsed;
        WelcomeContent.Visibility = Visibility.Collapsed;
        PredictionWaitingContent.Visibility = Visibility.Collapsed;
        PredictionStreamingContent.Visibility = Visibility.Collapsed;
        PredictionSuccessContent.Visibility = Visibility.Collapsed;
        DictationWaitingContent.Visibility = Visibility.Collapsed;
        DictationSuccessContent.Visibility = Visibility.Collapsed;
        UpdateBannerContent.Visibility = Visibility.Collapsed;
        ErrorContent.Visibility = Visibility.Collapsed;
    }

    private void ShowWithAnimation(UIElement element)
    {
        HideAll();
        element.Visibility = Visibility.Visible;
        var sb = (Storyboard)FindResource("FadeTransition");
        Storyboard.SetTarget(sb.Children[0], element);
        sb.Begin();
    }

    public void ResetToIdle()
    {
        ShowWithAnimation(IdleContent);
    }

    public void ShowWelcome()
    {
        ShowWithAnimation(WelcomeContent);
        // Auto-dismiss after 3s
        Task.Delay(3000).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() => ShowWithAnimation(IdleContent));
        });
    }

    public void ShowPredictionWaiting()
    {
        ShowWithAnimation(PredictionWaitingContent);
    }

    public void ShowPredictionStreaming(string text)
    {
        PredictionStreamText.Text = text;
        if (PredictionStreamingContent.Visibility != Visibility.Visible)
        {
            ShowWithAnimation(PredictionStreamingContent);
        }
    }

    public void ShowPredictionSuccess(string text)
    {
        ShowWithAnimation(PredictionSuccessContent);
    }

    public void ShowDictationWaiting()
    {
        ShowWithAnimation(DictationWaitingContent);
    }

    public void ShowDictationSuccess()
    {
        ShowWithAnimation(DictationSuccessContent);
    }

    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ShowWithAnimation(ErrorContent);
    }

    public void ShowUpdateBanner()
    {
        ShowWithAnimation(UpdateBannerContent);
    }
}
