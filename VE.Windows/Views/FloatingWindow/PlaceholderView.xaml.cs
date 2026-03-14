using System.Windows.Controls;

namespace VE.Windows.Views.FloatingWindow;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView()
    {
        InitializeComponent();
    }

    public void SetContent(string title, string subtitle)
    {
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
    }
}
