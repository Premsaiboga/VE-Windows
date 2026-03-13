using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace VE.Windows.Helpers;

/// <summary>
/// Auto-update service (equivalent to Sparkle on macOS).
/// Checks GitHub releases or custom feed for updates.
/// </summary>
public sealed class UpdateService : INotifyPropertyChanged
{
    public static UpdateService Instance { get; } = new();

    private bool _isUpdateAvailable;
    private string? _latestVersion;
    private string? _downloadUrl;
    private Timer? _checkTimer;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set { _isUpdateAvailable = value; OnPropertyChanged(); }
    }

    public string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public string? LatestVersion
    {
        get => _latestVersion;
        private set { _latestVersion = value; OnPropertyChanged(); }
    }

    private UpdateService()
    {
        // Check for updates every 4 hours
        _checkTimer = new Timer(async _ => await CheckForUpdates(),
            null, TimeSpan.FromMinutes(5), TimeSpan.FromHours(4));
    }

    public async Task CheckForUpdates()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VE-Windows/1.0");

            // Check custom update feed (matching Sparkle appcast.xml pattern)
            var feedUrl = "https://veaiinc.github.io/ve-windows-app-releases/latest.json";
            var response = await httpClient.GetStringAsync(feedUrl);
            var release = JsonConvert.DeserializeObject<UpdateInfo>(response);

            if (release?.Version != null && IsNewerVersion(release.Version))
            {
                LatestVersion = release.Version;
                _downloadUrl = release.DownloadUrl;
                IsUpdateAvailable = true;
                FileLogger.Instance.Info("Update", $"Update available: {release.Version}");
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Debug("Update", $"Update check failed: {ex.Message}");
        }
    }

    public void DownloadAndInstall()
    {
        if (_downloadUrl == null) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_downloadUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Update", $"Download failed: {ex.Message}");
        }
    }

    private bool IsNewerVersion(string latest)
    {
        try
        {
            var current = new Version(CurrentVersion);
            var remote = new Version(latest);
            return remote > current;
        }
        catch { return false; }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private class UpdateInfo
    {
        [JsonProperty("version")] public string? Version { get; set; }
        [JsonProperty("downloadUrl")] public string? DownloadUrl { get; set; }
        [JsonProperty("releaseNotes")] public string? ReleaseNotes { get; set; }
    }
}
