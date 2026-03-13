using System.Windows;
using System.Windows.Controls;

namespace VE.Windows.Views.Prediction;

public partial class PredictionView : UserControl
{
    public PredictionView()
    {
        InitializeComponent();

        ViewCoordinator.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewCoordinator.CombinedPredictionState) ||
                e.PropertyName == nameof(ViewCoordinator.PredictionText))
            {
                Dispatcher.Invoke(UpdateState);
            }
        };
    }

    private void UpdateState()
    {
        WaitingPanel.Visibility = Visibility.Collapsed;
        StreamingPanel.Visibility = Visibility.Collapsed;
        SuccessPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;

        switch (ViewCoordinator.Instance.CombinedPredictionState)
        {
            case CombinedPredictionState.Waiting:
                WaitingPanel.Visibility = Visibility.Visible;
                break;
            case CombinedPredictionState.Streaming:
                StreamingPanel.Visibility = Visibility.Visible;
                StreamingText.Text = ViewCoordinator.Instance.PredictionText ?? "";
                break;
            case CombinedPredictionState.Success:
                SuccessPanel.Visibility = Visibility.Visible;
                var text = ViewCoordinator.Instance.PredictionText ?? "";
                SuccessPreviewText.Text = text.Length > 100 ? text[..100] + "..." : text;
                break;
            case CombinedPredictionState.Error:
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorText.Text = ViewCoordinator.Instance.ErrorMessage ?? "Prediction failed";
                break;
        }
    }
}