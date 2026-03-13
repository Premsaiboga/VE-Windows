using System.Windows;
using System.Windows.Controls;
using VE.Windows.Managers;
using VE.Windows.Models;

namespace VE.Windows.Views.Auth;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private async void MicrosoftButton_Click(object sender, RoutedEventArgs e)
    {
        MicrosoftButton.IsEnabled = false;
        GoogleButton.IsEnabled = false;
        LoadingSpinner.Visibility = Visibility.Visible;

        await AuthManager.Instance.AuthenticateWithOutlook();

        // Re-enable after 5s in case browser auth fails
        await Task.Delay(5000);
        MicrosoftButton.IsEnabled = true;
        GoogleButton.IsEnabled = true;
        LoadingSpinner.Visibility = Visibility.Collapsed;
    }

    private async void GoogleButton_Click(object sender, RoutedEventArgs e)
    {
        MicrosoftButton.IsEnabled = false;
        GoogleButton.IsEnabled = false;
        LoadingSpinner.Visibility = Visibility.Visible;

        await AuthManager.Instance.AuthenticateWithGoogle();

        await Task.Delay(5000);
        MicrosoftButton.IsEnabled = true;
        GoogleButton.IsEnabled = true;
        LoadingSpinner.Visibility = Visibility.Collapsed;
    }

    private void TermsLink_Click(object sender, RoutedEventArgs e)
    {
        AppURLs.Open(AppURLType.TermsAndConditions);
    }

    private void PrivacyLink_Click(object sender, RoutedEventArgs e)
    {
        AppURLs.Open(AppURLType.PrivacyPolicy);
    }
}
