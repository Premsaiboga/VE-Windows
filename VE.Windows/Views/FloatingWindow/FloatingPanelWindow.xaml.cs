using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VE.Windows.Views.FloatingWindow;

public partial class FloatingPanelWindow : Window
{
    public FloatingPanelWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionBelowNotch();
    }

    private void PositionBelowNotch()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = (screenWidth - Width) / 2;
        Top = 40;
    }

    /// <summary>
    /// Allow window drag from any non-interactive area.
    /// Uses OnMouseLeftButtonDown override so it only fires when no child
    /// control (Button, TextBox, etc.) has already handled the event.
    /// </summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // If a Button/TextBox already handled the event, e.Handled is true
        // and this won't be reached for those controls. But for Border-based
        // click handlers that forget to set Handled, do an extra check:
        if (e.Handled) return;

        // Walk up from the click source — if it hits an interactive control, don't drag
        if (IsInteractiveSource(e.OriginalSource as DependencyObject))
            return;

        if (e.ClickCount == 1)
            DragMove();
    }

    private bool IsInteractiveSource(DependencyObject? source)
    {
        while (source != null && source != this)
        {
            if (source is ButtonBase or TextBoxBase or ScrollBar
                or Slider or ComboBox or ListBox)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void CloseButton_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Hide();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Don't auto-hide — let user close explicitly
    }

    public void UpdateShadowBackground(Brush bg)
    {
        if (WindowBorder.Parent is Grid g && g.Children.Count > 0 && g.Children[0] is Border shadowBorder)
        {
            shadowBorder.Background = bg;
        }
    }

    public void ShowAndActivate()
    {
        Show();
        Activate();
        PositionBelowNotch();
    }
}
