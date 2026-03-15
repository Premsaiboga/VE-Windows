using System.Runtime.InteropServices;
using System.Text;
using VE.Windows.Helpers;

namespace VE.Windows.Helpers;

/// <summary>
/// Windows UI Automation helper — equivalent to macOS XPCHelperClient.
/// Provides focused input field text, caret position, and active window info
/// using Windows UI Automation API and P/Invoke.
/// </summary>
public static class UIAutomationHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    /// <summary>
    /// Get info about the currently active (foreground) window.
    /// Equivalent to macOS XPCHelper getWindowContextForSocket.
    /// </summary>
    public static ActiveWindowInfo GetActiveWindowInfo()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return new ActiveWindowInfo();

        var titleLength = GetWindowTextLength(hwnd);
        var titleSb = new StringBuilder(titleLength + 1);
        GetWindowText(hwnd, titleSb, titleSb.Capacity);

        var classSb = new StringBuilder(256);
        GetClassName(hwnd, classSb, classSb.Capacity);

        GetWindowThreadProcessId(hwnd, out var processId);

        string? processName = null;
        try
        {
            var process = System.Diagnostics.Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch { }

        return new ActiveWindowInfo
        {
            WindowHandle = hwnd,
            Title = titleSb.ToString(),
            ClassName = classSb.ToString(),
            ProcessId = processId,
            ProcessName = processName
        };
    }

    /// <summary>
    /// Get the text content of the currently focused input field using UI Automation.
    /// Equivalent to macOS XPCHelper getFocusedInputFieldText.
    /// Returns null if no accessible input field is focused.
    /// </summary>
    public static string? GetFocusedInputFieldText()
    {
        try
        {
            var focused = System.Windows.Automation.AutomationElement.FocusedElement;
            if (focused == null) return null;

            // Try ValuePattern first (text boxes, inputs)
            if (focused.TryGetCurrentPattern(
                System.Windows.Automation.ValuePattern.Pattern,
                out var valuePatternObj))
            {
                var valuePattern = (System.Windows.Automation.ValuePattern)valuePatternObj;
                return valuePattern.Current.Value;
            }

            // Try TextPattern (rich text editors)
            if (focused.TryGetCurrentPattern(
                System.Windows.Automation.TextPattern.Pattern,
                out var textPatternObj))
            {
                var textPattern = (System.Windows.Automation.TextPattern)textPatternObj;
                return textPattern.DocumentRange.GetText(-1);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Debug("UIAutomation", $"GetFocusedInputFieldText failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Get the caret position in the currently focused text field.
    /// Equivalent to macOS XPCHelper caret position via TextPattern.
    /// Returns -1 if caret position cannot be determined.
    /// </summary>
    public static int GetCaretPosition()
    {
        try
        {
            var focused = System.Windows.Automation.AutomationElement.FocusedElement;
            if (focused == null) return -1;

            if (focused.TryGetCurrentPattern(
                System.Windows.Automation.TextPattern.Pattern,
                out var textPatternObj))
            {
                var textPattern = (System.Windows.Automation.TextPattern)textPatternObj;
                var selection = textPattern.GetSelection();
                if (selection.Length > 0)
                {
                    // Get the start of selection as caret position
                    var selectionRange = selection[0];
                    var fullRange = textPattern.DocumentRange;
                    var beforeCaret = fullRange.Clone();
                    beforeCaret.MoveEndpointByRange(
                        System.Windows.Automation.Text.TextPatternRangeEndpoint.End,
                        selectionRange,
                        System.Windows.Automation.Text.TextPatternRangeEndpoint.Start);
                    return beforeCaret.GetText(-1).Length;
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Debug("UIAutomation", $"GetCaretPosition failed: {ex.Message}");
        }
        return -1;
    }

    /// <summary>
    /// Capture comprehensive app context for prediction/dictation.
    /// Combines active window title, focused element info, selected text.
    /// Equivalent to macOS XPCHelper captureAppActivity.
    /// </summary>
    public static AppContext CaptureAppContext()
    {
        var windowInfo = GetActiveWindowInfo();
        var focusedText = GetFocusedInputFieldText();
        var caretPos = GetCaretPosition();

        return new AppContext
        {
            WindowTitle = windowInfo.Title,
            AppName = windowInfo.ProcessName,
            ClassName = windowInfo.ClassName,
            FocusedFieldText = focusedText,
            CaretPosition = caretPos
        };
    }
}

/// <summary>
/// Information about the currently active window.
/// </summary>
public class ActiveWindowInfo
{
    public IntPtr WindowHandle { get; init; }
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public uint ProcessId { get; init; }
    public string? ProcessName { get; init; }
}

/// <summary>
/// Comprehensive app context for AI features.
/// </summary>
public class AppContext
{
    public string? WindowTitle { get; init; }
    public string? AppName { get; init; }
    public string? ClassName { get; init; }
    public string? FocusedFieldText { get; init; }
    public int CaretPosition { get; init; } = -1;
}
