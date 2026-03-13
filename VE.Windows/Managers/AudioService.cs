using System.ComponentModel;
using System.Runtime.CompilerServices;
using NAudio.Wave;
using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Managers;

/// <summary>
/// Audio capture service using NAudio.
/// Equivalent to macOS UnifiedAudioService + AudioHelper.
/// </summary>
public sealed class AudioService : INotifyPropertyChanged, IDisposable
{
    public static AudioService Instance { get; } = new();

    private WaveInEvent? _waveIn;
    private bool _isRecording;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<byte[]>? OnAudioDataAvailable;

    public bool IsRecording
    {
        get => _isRecording;
        private set { _isRecording = value; OnPropertyChanged(); }
    }

    public List<AudioDevice> AvailableMicrophones { get; private set; } = new();
    public AudioDevice? SelectedMicrophone { get; private set; }

    private AudioService()
    {
        RefreshDevices();
    }

    public void RefreshDevices()
    {
        AvailableMicrophones.Clear();

        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            AvailableMicrophones.Add(new AudioDevice
            {
                Id = i,
                Name = caps.ProductName,
                Channels = caps.Channels,
                IsDefault = i == 0
            });
        }

        // Select based on settings
        var selectedUid = SettingsManager.Instance.SelectedMicrophoneUID;
        if (selectedUid == "system-default" || selectedUid == null)
        {
            SelectedMicrophone = AvailableMicrophones.FirstOrDefault(d => d.IsDefault)
                                 ?? AvailableMicrophones.FirstOrDefault();
        }
        else
        {
            var selectedName = SettingsManager.Instance.SelectedMicrophoneName;
            SelectedMicrophone = AvailableMicrophones.FirstOrDefault(d => d.Name == selectedName)
                                 ?? AvailableMicrophones.FirstOrDefault();
        }

        OnPropertyChanged(nameof(AvailableMicrophones));
        OnPropertyChanged(nameof(SelectedMicrophone));
    }

    public void SetMicrophone(int deviceId)
    {
        var device = AvailableMicrophones.FirstOrDefault(d => d.Id == deviceId);
        if (device != null)
        {
            SelectedMicrophone = device;
            SettingsManager.Instance.SelectedMicrophoneUID = device.Name;
            SettingsManager.Instance.SelectedMicrophoneName = device.Name;
            OnPropertyChanged(nameof(SelectedMicrophone));
        }
    }

    public void StartCapture()
    {
        if (IsRecording) return;

        try
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = SelectedMicrophone?.Id ?? 0,
                WaveFormat = new WaveFormat(16000, 16, 1), // 16kHz, 16-bit, mono
                BufferMilliseconds = 100
            };

            _waveIn.DataAvailable += (s, e) =>
            {
                if (e.BytesRecorded > 0)
                {
                    var buffer = new byte[e.BytesRecorded];
                    Array.Copy(e.Buffer, buffer, e.BytesRecorded);
                    OnAudioDataAvailable?.Invoke(this, buffer);
                }
            };

            _waveIn.RecordingStopped += (s, e) =>
            {
                IsRecording = false;
                if (e.Exception != null)
                {
                    FileLogger.Instance.Error("Audio", $"Recording error: {e.Exception.Message}");
                }
            };

            _waveIn.StartRecording();
            IsRecording = true;
            FileLogger.Instance.Info("Audio", $"Recording started: {SelectedMicrophone?.Name ?? "default"}");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Audio", $"Failed to start recording: {ex.Message}");
            IsRecording = false;
        }
    }

    public void StopCapture()
    {
        if (!IsRecording) return;

        try
        {
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveIn = null;
            IsRecording = false;
            FileLogger.Instance.Info("Audio", "Recording stopped");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Audio", $"Failed to stop recording: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopCapture();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class AudioDevice
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Channels { get; set; }
    public bool IsDefault { get; set; }
    public bool IsBluetooth => Name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
}
