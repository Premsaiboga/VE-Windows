using System.IdentityModel.Tokens.Jwt;
using Newtonsoft.Json;
using VE.Windows.Helpers;
using VE.Windows.Managers;

namespace VE.Windows.Services;

/// <summary>
/// Proactive JWT token refresh - refreshes 60 seconds before expiry.
/// Equivalent to macOS TokenRefreshService.
/// </summary>
public sealed class TokenRefreshService : IDisposable
{
    public static TokenRefreshService Instance { get; } = new();

    private Timer? _refreshTimer;
    private Timer? _checkTimer;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private const int RefreshBufferSeconds = 60;
    private const int CheckIntervalSeconds = 30;

    private TokenRefreshService()
    {
        // Check every 30 seconds
        _checkTimer = new Timer(OnCheckTimer, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(CheckIntervalSeconds));
    }

    private async void OnCheckTimer(object? state)
    {
        if (!AuthManager.Instance.IsAuthenticated) return;

        var token = AuthManager.Instance.Storage.UserToken;
        if (string.IsNullOrEmpty(token)) return;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            var expiresAt = jwtToken.ValidTo;
            var timeToExpiry = expiresAt - DateTime.UtcNow;

            if (timeToExpiry.TotalSeconds <= RefreshBufferSeconds)
            {
                FileLogger.Instance.Info("TokenRefresh", $"Token expires in {timeToExpiry.TotalSeconds:F0}s, refreshing...");
                await RefreshToken();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("TokenRefresh", $"Token check failed: {ex.Message}");
        }
    }

    public void ScheduleRefresh(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            var expiresAt = jwtToken.ValidTo;
            var refreshAt = expiresAt.AddSeconds(-RefreshBufferSeconds);
            var delay = refreshAt - DateTime.UtcNow;

            if (delay.TotalSeconds > 0)
            {
                _refreshTimer?.Dispose();
                _refreshTimer = new Timer(async _ => await RefreshToken(), null, delay, Timeout.InfiniteTimeSpan);
                FileLogger.Instance.Info("TokenRefresh", $"Scheduled refresh in {delay.TotalMinutes:F1}m");
            }
            else
            {
                // Already past refresh time, refresh now
                Task.Run(async () => await RefreshToken());
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("TokenRefresh", $"Schedule failed: {ex.Message}");
        }
    }

    public async Task<bool> RefreshToken()
    {
        if (!await _refreshLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            FileLogger.Instance.Warning("TokenRefresh", "Refresh already in progress");
            return false;
        }

        try
        {
            var authUrl = BaseURLService.Instance.GetGlobalUrl("auth");
            if (authUrl == null) return false;

            var url = $"{authUrl}/refresh-token";
            var response = await NetworkService.Instance.PostRawAsync(url);
            if (response == null) return false;

            var result = JsonConvert.DeserializeObject<TokenRefreshResponse>(response);
            if (result?.AccessToken == null) return false;

            AuthManager.Instance.Storage.UserToken = result.AccessToken;
            if (result.CsrfToken != null)
            {
                AuthManager.Instance.Storage.CSRFToken = result.CsrfToken;
            }

            ScheduleRefresh(result.AccessToken);
            FileLogger.Instance.Info("TokenRefresh", "Token refreshed successfully");
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("TokenRefresh", $"Refresh failed: {ex.Message}");
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public static DateTime? GetTokenExpiry(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            return jwtToken.ValidTo;
        }
        catch { return null; }
    }

    public static bool IsTokenValid(string? token)
    {
        var expiry = GetTokenExpiry(token);
        return expiry.HasValue && expiry.Value > DateTime.UtcNow;
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _checkTimer?.Dispose();
        _refreshLock.Dispose();
    }

    private class TokenRefreshResponse
    {
        [JsonProperty("accessToken")] public string? AccessToken { get; set; }
        [JsonProperty("csrfToken")] public string? CsrfToken { get; set; }
    }
}
