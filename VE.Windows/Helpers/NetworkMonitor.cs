using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace VE.Windows.Helpers;

public sealed class NetworkMonitor : INotifyPropertyChanged
{
    public static NetworkMonitor Instance { get; } = new();

    private bool _isConnected;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<bool>? NetworkChanged;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged();
                NetworkChanged?.Invoke(this, value);
            }
        }
    }

    private NetworkMonitor()
    {
        _isConnected = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += (s, e) =>
        {
            IsConnected = e.IsAvailable;
            FileLogger.Instance.Info("Network", $"Network {(e.IsAvailable ? "connected" : "disconnected")}");
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
