using System.Windows;
using System.Windows.Controls;
using VE.Windows.Services;

namespace VE.Windows.Views.Dictation;

public partial class DictationView : UserControl
{
    public DictationView()
    {
        InitializeComponent();

        DictationService.Instance.PropertyChanged += (s, e) =>
        {
            Dispatcher.Invoke(UpdateState);
        };
    }

    private void UpdateState()
    {
        WaitingPanel.Visibility = Visibility.Collapsed;
        RecordingPanel.Visibility = Visibility.Collapsed;
        ProcessingPanel.Visibility = Visibility.Collapsed;
        SuccessPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;

        var service = DictationService.Instance;

        switch (service.State)
        {
            case DictationState.Waiting:
                WaitingPanel.Visibility = Visibility.Visible;
                break;
            case DictationState.Recording:
                RecordingPanel.Visibility = Visibility.Visible;
                LiveTranscription.Text = service.TranscribedText;
                break;
            case DictationState.Processing:
                ProcessingPanel.Visibility = Visibility.Visible;
                break;
            case DictationState.Success:
                SuccessPanel.Visibility = Visibility.Visible;
                var text = service.TranscribedText;
                TranscribedPreview.Text = text.Length > 150 ? text[..150] + "..." : text;
                break;
            case DictationState.Error:
                ErrorPanel.Visibility = Visibility.Visible;
                DictErrorText.Text = service.ErrorMessage ?? "Dictation failed";
                break;
        }
    }
}
