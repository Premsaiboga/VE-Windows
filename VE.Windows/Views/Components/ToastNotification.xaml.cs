using System.Windows.Controls;

namespace VE.Windows.Views.Components;

public partial class ToastNotification : UserControl
{
    public ToastNotification()
    {
        InitializeComponent();
    }

    public void ShowMessage(string message, string icon = "\u2139")
    {
        ToastMessage.Text = message;
        ToastIcon.Text = icon;
    }

    public void ShowSuccess(string message) => ShowMessage(message, "\u2713");
    public void ShowError(string message) => ShowMessage(message, "\u26A0");
    public void ShowWarning(string message) => ShowMessage(message, "\u26A0");
}
