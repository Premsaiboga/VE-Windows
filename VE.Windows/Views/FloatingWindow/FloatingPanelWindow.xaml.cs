using System.Windows;
using System.Windows.Input;
using VE.Windows.Helpers;
using VE.Windows.Views.Settings;

namespace VE.Windows.Views.FloatingWindow;

public partial class FloatingPanelWindow : Window
{
    private string _currentTab = "Chat";

    public FloatingPanelWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionBelowNotch();
        UpdateTabSelection();
    }

    private void PositionBelowNotch()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = (screenWidth - Width) / 2;
        Top = 40; // Just below the notch
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Don't auto-hide — let user close explicitly or via Escape
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string tab)
        {
            _currentTab = tab;
            SwitchTab(tab);
            UpdateTabSelection();
        }
    }

    private void SwitchTab(string tab)
    {
        // Hide all content
        ChatContent.Visibility = Visibility.Collapsed;
        SearchContent.Visibility = Visibility.Collapsed;
        NotesContent.Visibility = Visibility.Collapsed;
        ConnectorsContent.Visibility = Visibility.Collapsed;
        KnowledgeContent.Visibility = Visibility.Collapsed;
        ChatsContent.Visibility = Visibility.Collapsed;
        MailContent.Visibility = Visibility.Collapsed;
        PredictionContent.Visibility = Visibility.Collapsed;
        IntentModelContent.Visibility = Visibility.Collapsed;

        // Show selected tab content and update title
        switch (tab)
        {
            case "Chat":
                ChatContent.Visibility = Visibility.Visible;
                TitleText.Text = "Chat";
                break;
            case "Search":
                SearchContent.Visibility = Visibility.Visible;
                TitleText.Text = "Search Files";
                break;
            case "Notes":
                NotesContent.Visibility = Visibility.Visible;
                TitleText.Text = "Meeting Notes";
                break;
            case "Chats":
                ChatsContent.Visibility = Visibility.Visible;
                TitleText.Text = "Recent Chats";
                break;
            case "Connectors":
                ConnectorsContent.Visibility = Visibility.Visible;
                TitleText.Text = "Connectors";
                break;
            case "Knowledge":
                KnowledgeContent.Visibility = Visibility.Visible;
                TitleText.Text = "Knowledge Base";
                break;
            case "Mail":
                MailContent.Visibility = Visibility.Visible;
                TitleText.Text = "Intent Email";
                break;
            case "Prediction":
                PredictionContent.Visibility = Visibility.Visible;
                TitleText.Text = "Predictions";
                break;
            case "IntentModel":
                IntentModelContent.Visibility = Visibility.Visible;
                TitleText.Text = "Intent Model";
                break;
        }
    }

    private void UpdateTabSelection()
    {
        var buttons = new[] { TabChat, TabSearch, TabNotes, TabChats, TabConnectors, TabKnowledge, TabMail, TabPrediction, TabIntentModel };
        foreach (var btn in buttons)
        {
            btn.Opacity = (btn.Tag as string) == _currentTab ? 1.0 : 0.5;
        }
    }

    private SettingsWindow? _settingsWindow;

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow();
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowAndActivate()
    {
        Show();
        Activate();
        PositionBelowNotch();
    }
}
