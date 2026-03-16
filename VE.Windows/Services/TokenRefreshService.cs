using System.IdentityModel.Tokens.Jwt;
using Newtonsoft.Json;
using VE.Windows.Helpers;
using VE.Windows.Managers;

namespace VE.Windows.Services;

/// <summary>
/// Proactive JWT token refresh with guard against duplicate concurrent refreshes.
/// Matches macOS AuthManager+TokenRefresh: scheduled timer, 30-second periodic check,
/// SemaphoreSlim guard so only one refresh executes at a time (others await the same result).
/// </summary>
public sealed class TokenRefreshService : IDisposable
{
    public static TokenRefreshService Instance { get; } = new();

    private Timer? _refreshTimer;
    private Timer? _checkTimer;
    private const int RefreshBufferSeconds = 60;
    private const int CheckIntervalSeconds = 30;

    // Guard: prevents duplicate concurrent token refreshes (matches macOS RefreshTokenGuard actor)
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Task<bool>? _activeRefreshTask;

    private TokenRefreshService()
    {
        // Check every 30 seconds (matches macOS periodic check)
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

            // macOS uses 180s aggressive threshold; we use 60s for scheduled + 30s check interval
            if (timeToExpiry.TotalSeconds <= RefreshBufferSeconds)
            {
                FileLogger.Instance.Info("TokenRefresh", $"Token expires in {timeToExpiry.TotalSeconds:F0}s, refreshing...");
                await EnsureTokenRefreshed();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("TokenRefresh", $"Token check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Schedule a proactive refresh before the token expires.
    /// </summary>
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
                _refreshTimer = new Timer(async _ => await EnsureTokenRefreshed(), null, delay, Timeout.InfiniteTimeSpan);
                FileLogger.Instance.Info("TokenRefresh", $"Scheduled refresh in {delay.TotalMinutes:F1}m");
            }
            else
            {
                // Already past refresh time, refresh now
                Task.Run(async () => await EnsureTokenRefreshed());
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("TokenRefresh", $"Schedule failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Guarded token refresh: ensures only one refresh executes at a time.
    /// If a refresh is already in progress, callers await the same result.
    /// Matches macOS RefreshTokenGuard actor pattern.
    /// </summary>
    public async Task<bool> EnsureTokenRefreshed()
    {
        // Fast path: if there's already an active refresh, await it
        var existing = _activeRefreshTask;
        if (existing != null)
        {
            FileLogger.Instance.Debug("TokenRefresh", "Awaiting existing refresh...");
            return await existing;
        }

        await _refreshLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_activeRefreshTask != null) return await _activeRefreshTask;

            _activeRefreshTask = RefreshTokenInternal();
            return await _activeRefreshTask;
        }
        finally
        {
            _activeRefreshTask = null;
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Legacy method for backward compatibility. Prefer EnsureTokenRefreshed().
    /// </summary>
    public Task<bool> RefreshToken() => EnsureTokenRefreshed();

    private async Task<bool> RefreshTokenInternal()
    {
        try
        {
            var authUrl = BaseURLService.Instance.GetGlobalUrl("auth");
            if (authUrl == null)
            {
                FileLogger.Instance.Error("TokenRefresh", "No auth URL configured");
                return false;
            }

            var url = $"{authUrl}/refresh-token";
            FileLogger.Instance.Info("TokenRefresh", $"Calling {url}...");

            // Log cookie count for debugging
            var cookieCount = NetworkService.Instance.GetCookieCount(new Uri(authUrl));
            FileLogger.Instance.Debug("TokenRefresh", $"Cookies for {authUrl}: {cookieCount}");

            var response = await NetworkService.Instance.PostRawAsync(url);
            if (response == null)
            {
                FileLogger.Instance.Error("TokenRefresh", "Refresh endpoint returned null (network error)");
                return false;
            }

            FileLogger.Instance.Debug("TokenRefresh", $"Refresh response ({response.Length} chars): {response.Substring(0, Math.Min(200, response.Length))}");

            // Server returns {"tokens":{"accessToken":"...","csrfToken":"..."}}
            // Parse with wrapper support
            var json = Newtonsoft.Json.Linq.JObject.Parse(response);
            var accessToken = json["tokens"]?["accessToken"]?.Value<string>()
                ?? json["accessToken"]?.Value<string>();
            var csrfToken = json["tokens"]?["csrfToken"]?.Value<string>()
                ?? json["csrfToken"]?.Value<string>();

            if (string.IsNullOrEmpty(accessToken))
            {
                FileLogger.Instance.Error("TokenRefresh", $"No accessToken in refresh response");
                return false;
            }

            AuthManager.Instance.Storage.UserToken = accessToken;
            if (csrfToken != null)
            {
                AuthManager.Instance.Storage.CSRFToken = csrfToken;
            }

            ScheduleRefresh(accessToken);
            FileLogger.Instance.Info("TokenRefresh", "Token refreshed successfully");
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("TokenRefresh", $"Refresh failed: {ex.Message}");
            return false;
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
