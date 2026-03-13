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
            UpdateTabSelection();
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
