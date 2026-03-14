using System.Windows;
using System.Windows.Input;
using VE.Windows.Helpers;

namespace VE.Windows.Views.FloatingWindow;

public partial class FloatingPanelWindow : Window
{
    private string _currentTab = "Chat";

    public FloatingPanelWindow()
    {
        InitializeComponent();

        // Set placeholder content
        ConnectorsContent.SetContent("Connectors", "Connect your apps and services\nSlack, Notion, Gmail, Jira & more");
        KnowledgeContent.SetContent("Knowledge", "Train your AI with custom knowledge\nUpload files and documents");
        VoiceContent.SetContent("Voice", "Voice settings and enrollment\nCustomize voice commands");
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
            TitleText.Text = tab;
            SwitchTab(tab);
            UpdateTabSelection();
        }
    }

    private void SwitchTab(string tab)
    {
        // Hide all content
        ChatContent.Visibility = Visibility.Collapsed;
        NotesContent.Visibility = Visibility.Collapsed;
        ConnectorsContent.Visibility = Visibility.Collapsed;
        KnowledgeContent.Visibility = Visibility.Collapsed;
        VoiceContent.Visibility = Visibility.Collapsed;

        // Show selected tab content
        switch (tab)
        {
            case "Chat":
                ChatContent.Visibility = Visibility.Visible;
                break;
            case "Notes":
                NotesContent.Visibility = Visibility.Visible;
                break;
            case "Connectors":
                ConnectorsContent.Visibility = Visibility.Visible;
                break;
            case "Knowledge":
                KnowledgeContent.Visibility = Visibility.Visible;
                break;
            case "Voice":
                VoiceContent.Visibility = Visibility.Visible;
                break;
        }
    }

    private void UpdateTabSelection()
    {
        // Visual feedback for selected tab
        var buttons = new[] { TabChat, TabNotes, TabConnectors, TabKnowledge, TabVoice };
        foreach (var btn in buttons)
        {
            btn.Opacity = (btn.Tag as string) == _currentTab ? 1.0 : 0.5;
        }
    }

    public void ShowAndActivate()
    {
        Show();
        Activate();
        PositionBelowNotch();
    }
}
