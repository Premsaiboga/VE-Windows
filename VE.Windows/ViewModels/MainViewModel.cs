using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace VE.Windows.ViewModels;

public enum NotchState { Closed, Open }

/// <summary>
/// Main view model for the floating notch window.
/// Equivalent to macOS VEAIViewModel.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private NotchState _notchState = NotchState.Closed;
    private double _notchWidth = 220;
    private double _notchHeight = 36;
    private double _openWidth = 380;
    private double _openHeight = 500;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? NotchOpened;
    public event EventHandler? NotchClosed;

    public NotchState NotchState
    {
        get => _notchState;
        set { _notchState = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsOpen)); }
    }

    public bool IsOpen => _notchState == NotchState.Open;

    public double NotchWidth
    {
        get => _notchState == NotchState.Open ? _openWidth : _notchWidth;
        set { _notchWidth = value; OnPropertyChanged(); }
    }

    public double NotchHeight
    {
        get => _notchState == NotchState.Open ? _openHeight : _notchHeight;
        set { _notchHeight = value; OnPropertyChanged(); }
    }

    public double ClosedWidth
    {
        get => _notchWidth;
        set { _notchWidth = value; OnPropertyChanged(); }
    }

    public double ClosedHeight
    {
        get => _notchHeight;
        set { _notchHeight = value; OnPropertyChanged(); }
    }

    public double OpenWidth
    {
        get => _openWidth;
        set { _openWidth = value; OnPropertyChanged(); }
    }

    public double OpenHeight
    {
        get => _openHeight;
        set { _openHeight = value; OnPropertyChanged(); }
    }

    public void Open()
    {
        if (_notchState == NotchState.Open) return;
        NotchState = NotchState.Open;
        OnPropertyChanged(nameof(NotchWidth));
        OnPropertyChanged(nameof(NotchHeight));
        NotchOpened?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        if (_notchState == NotchState.Closed) return;
        NotchState = NotchState.Closed;
        OnPropertyChanged(nameof(NotchWidth));
        OnPropertyChanged(nameof(NotchHeight));
        NotchClosed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle()
    {
        if (_notchState == NotchState.Open) Close();
        else Open();
    }

    /// <summary>
    /// Calculate window position centered at top of primary screen.
    /// </summary>
    public Point GetWindowPosition()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var x = (screenWidth - NotchWidth) / 2;
        return new Point(x, 0);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
