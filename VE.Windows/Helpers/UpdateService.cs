using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace VE.Windows.Helpers;

/// <summary>
/// Auto-update service (equivalent to Sparkle on macOS).
/// Checks update feed every 10 minutes, downloads installer, prompts user.
/// </summary>
public sealed class UpdateService : INotifyPropertyChanged
{
    public static UpdateService Instance { get; } = new();

    private bool _isUpdateAvailable;
    private bool _isDownloading;
    private double _downloadProgress;
    private string? _latestVersion;
    private string? _downloadUrl;
    private string? _releaseNotes;
    private string? _downloadedInstallerPath;
    private Timer? _checkTimer;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set { _isUpdateAvailable = value; OnPropertyChanged(); }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set { _isDownloading = value; OnPropertyChanged(); }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set { _downloadProgress = value; OnPropertyChanged(); }
    }

    public string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public string? LatestVersion
    {
        get => _latestVersion;
        private set { _latestVersion = value; OnPropertyChanged(); }
    }

    public string? ReleaseNotes
    {
        get => _releaseNotes;
        private set { _releaseNotes = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether to auto-install updates on next restart. User-togglable in settings.
    /// </summary>
    public bool AutoInstallEnabled
    {
        get => Models.SettingsManager.Instance.Get("AutoInstallUpdates", true);
        set
        {
            Models.SettingsManager.Instance.Set("AutoInstallUpdates", value);
            OnPropertyChanged();
        }
    }

    private UpdateService()
    {
        // Check for updates every 10 minutes (matching macOS Sparkle frequency)
        _checkTimer = new Timer(async _ => await CheckForUpdates(),
            null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
    }

    public async Task CheckForUpdates()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(15);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"VE-Windows/{CurrentVersion}");

            var feedUrl = "https://veaiinc.github.io/ve-windows-app-releases/latest.json";
            var response = await httpClient.GetStringAsync(feedUrl);
            var release = JsonConvert.DeserializeObject<UpdateInfo>(response);

            if (release?.Version != null && IsNewerVersion(release.Version))
            {
                LatestVersion = release.Version;
                _downloadUrl = release.DownloadUrl;
                ReleaseNotes = release.ReleaseNotes;
                IsUpdateAvailable = true;

                // Show update banner in notch
                ViewCoordinator.Instance.IsUpdateBannerVisible = true;

                FileLogger.Instance.Info("Update", $"Update available: {release.Version}");

                // Auto-download if enabled
                if (AutoInstallEnabled && _downloadedInstallerPath == null)
                {
                    _ = DownloadInstaller();
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Debug("Update", $"Update check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Download the installer to a temp path. Does not install yet.
    /// </summary>
    public async Task DownloadInstaller()
    {
        if (_downloadUrl == null || IsDownloading) return;
        if (_downloadedInstallerPath != null && File.Exists(_downloadedInstallerPath)) return;

        IsDownloading = true;
        DownloadProgress = 0;

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            var tempPath = Path.Combine(Path.GetTempPath(), $"VE_Update_{LatestVersion}.exe");

            using var response = await httpClient.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;
                if (totalBytes > 0)
                {
                    DownloadProgress = (double)totalRead / totalBytes;
                }
            }

            _downloadedInstallerPath = tempPath;
            DownloadProgress = 1.0;
            FileLogger.Instance.Info("Update", $"Installer downloaded: {tempPath}");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Update", $"Download failed: {ex.Message}");
            _downloadedInstallerPath = null;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// Launch downloaded installer and exit the app. Called on user action or on restart.
    /// </summary>
    public void InstallAndRestart()
    {
        if (_downloadedInstallerPath == null || !File.Exists(_downloadedInstallerPath))
        {
            // Fallback: open download URL in browser
            if (_downloadUrl != null)
            {
                Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true });
            }
            return;
        }

        try
        {
            FileLogger.Instance.Info("Update", $"Launching installer: {_downloadedInstallerPath}");
            Process.Start(new ProcessStartInfo(_downloadedInstallerPath)
            {
                UseShellExecute = true
            });

            // Exit app so installer can replace files
            DispatcherHelper.RunOnUI(() =>
            {
                System.Windows.Application.Current?.Shutdown();
            });
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Update", $"Install failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if a downloaded installer is pending from a previous session.
    /// Called on startup when AutoInstallEnabled is true.
    /// </summary>
    public void CheckPendingInstall()
    {
        if (!AutoInstallEnabled) return;

        try
        {
            var tempDir = Path.GetTempPath();
            var installers = Directory.GetFiles(tempDir, "VE_Update_*.exe");
            foreach (var installer in installers)
            {
                // Clean up old installers
                try { File.Delete(installer); }
                catch (Exception) { } // Best-effort cleanup of temp files
            }
        }
        catch (Exception) { } // Best-effort cleanup
    }

    /// <summary>
    /// Dismiss the update banner without installing.
    /// </summary>
    public void DismissUpdateBanner()
    {
        ViewCoordinator.Instance.IsUpdateBannerVisible = false;
    }

    /// <summary>
    /// Open browser to download URL as fallback.
    /// </summary>
    public void DownloadAndInstall()
    {
        if (_downloadedInstallerPath != null && File.Exists(_downloadedInstallerPath))
        {
            InstallAndRestart();
            return;
        }

        if (_downloadUrl == null) return;

        try
        {
            Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true });
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
