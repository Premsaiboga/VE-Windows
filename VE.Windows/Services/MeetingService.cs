using System.ComponentModel;
using System.Runtime.CompilerServices;
using VE.Windows.Helpers;
using VE.Windows.Models;

namespace VE.Windows.Services;

public enum MeetingState
{
    Inactive,
    Starting,
    Active,
    Paused,
    Result,
    Error
}

public sealed class MeetingService : INotifyPropertyChanged
{
    public static MeetingService Instance { get; } = new();

    private MeetingState _state = MeetingState.Inactive;
    private MeetingResult? _currentResult;
    private DateTime? _startTime;
    private Timer? _durationTimer;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? MeetingListNeedsRefresh;

    public MeetingState State
    {
        get => _state;
        private set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsActive)); }
    }

    public bool IsActive => _state == MeetingState.Active || _state == MeetingState.Paused;

    public MeetingResult? CurrentResult
    {
        get => _currentResult;
        private set { _currentResult = value; OnPropertyChanged(); }
    }

    public TimeSpan Duration => _startTime.HasValue ? DateTime.UtcNow - _startTime.Value : TimeSpan.Zero;

    private MeetingService() { }

    public async Task StartMeeting()
    {
        if (IsActive) return;

        State = MeetingState.Starting;
        FileLogger.Instance.Info("Meeting", "Starting meeting notes...");

        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("meeting_api");
            if (baseUrl == null) { State = MeetingState.Error; return; }

            var response = await NetworkService.Instance.PostAsync<MeetingStartResponse>(
                $"{baseUrl}/meetings/start");

            if (response != null)
            {
                _startTime = DateTime.UtcNow;
                State = MeetingState.Active;

                // Start duration timer
                _durationTimer = new Timer(_ => OnPropertyChanged(nameof(Duration)),
                    null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

                // Start audio capture
                Managers.AudioService.Instance.StartCapture();

                FileLogger.Instance.Info("Meeting", "Meeting started");
            }
            else
            {
                State = MeetingState.Error;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Meeting", $"Start failed: {ex.Message}");
            State = MeetingState.Error;
        }
    }

    public async Task StopMeeting()
    {
        if (!IsActive) return;

        FileLogger.Instance.Info("Meeting", "Stopping meeting...");
        Managers.AudioService.Instance.StopCapture();
        _durationTimer?.Dispose();
        _durationTimer = null;

        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("meeting_api");
            if (baseUrl != null)
            {
                var result = await NetworkService.Instance.PostAsync<MeetingResult>(
                    $"{baseUrl}/meetings/stop");
                if (result != null)
                {
                    CurrentResult = result;
                    State = MeetingState.Result;
                    MeetingListNeedsRefresh?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Meeting", $"Stop failed: {ex.Message}");
        }

        State = MeetingState.Inactive;
    }

    public void PauseMeeting()
    {
        if (State == MeetingState.Active)
        {
            State = MeetingState.Paused;
            Managers.AudioService.Instance.StopCapture();
        }
    }

    public void ResumeMeeting()
    {
        if (State == MeetingState.Paused)
        {
            State = MeetingState.Active;
            Managers.AudioService.Instance.StartCapture();
        }
    }

    public void DismissResult()
    {
        State = MeetingState.Inactive;
        CurrentResult = null;
        _startTime = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private class MeetingStartResponse
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
    }
}
