using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Infrastructure;
using VE.Windows.Managers;

namespace VE.Windows.Services;

/// <summary>
/// HTTP networking layer with auth headers, CSRF tokens, HTTPOnly cookie handling,
/// and automatic 401/406 retry logic.
/// Matches macOS NetworkService exactly: x-access-token header, x-csrf-token for
/// state-changing methods, cookie-based refresh token flow, single retry on token expiry.
///
/// Cookie persistence: On macOS, HTTPCookieStorage.shared auto-persists cookies to disk.
/// On Windows, CookieContainer is in-memory only, so we manually persist cookies to disk
/// (DPAPI-encrypted in Release, plain JSON in Debug) to survive app restarts.
/// </summary>
public sealed class NetworkService : INetworkService
{
    public static NetworkService Instance { get; } = new();

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private readonly string _cookiePersistPath;
    private readonly object _cookieLock = new();

    private NetworkService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var veDir = Path.Combine(appData, "VE");
        Directory.CreateDirectory(veDir);
#if DEBUG
        _cookiePersistPath = Path.Combine(veDir, "cookies_debug.json");
#else
        _cookiePersistPath = Path.Combine(veDir, "cookies.dat");
#endif

        _cookieContainer = new CookieContainer();
        RestoreCookiesFromDisk();

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VE-Windows/1.0");
    }

    /// <summary>
    /// Scope cookies to the .ve.ai domain to match macOS behavior.
    /// Called after login to ensure refresh token cookies are properly scoped.
    /// </summary>
    public void ScopeCookiesToDomain(Uri baseUri)
    {
        try
        {
            var domain = baseUri.Host;
            // Ensure cookies are scoped to the API domain
            foreach (Cookie cookie in _cookieContainer.GetCookies(baseUri))
            {
                cookie.Domain = domain;
                cookie.Secure = true;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warning("Network", $"Cookie scoping failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Get number of cookies stored for a given URI (for debugging refresh token issues).
    /// </summary>
    public int GetCookieCount(Uri uri)
    {
        try { return _cookieContainer.GetCookies(uri).Count; }
        catch { return -1; }
    }

    /// <summary>
    /// Persist cookies for the given domain to disk after a response that may contain Set-Cookie headers.
    /// Matches macOS HTTPCookieStorage.shared which auto-persists.
    /// </summary>
    public void PersistCookies(Uri uri)
    {
        try
        {
            var cookies = _cookieContainer.GetCookies(uri);
            if (cookies.Count == 0) return;

            var list = new List<SerializedCookie>();
            foreach (Cookie c in cookies)
            {
                // Only persist non-expired cookies
                if (c.Expired) continue;
                list.Add(new SerializedCookie
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = c.Path,
                    Expires = c.Expires,
                    Secure = c.Secure,
                    HttpOnly = c.HttpOnly
                });
            }

            var json = JsonConvert.SerializeObject(list);
            lock (_cookieLock)
            {
#if DEBUG
                File.WriteAllText(_cookiePersistPath, json);
#else
                var bytes = Encoding.UTF8.GetBytes(json);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_cookiePersistPath, encrypted);
#endif
            }
            FileLogger.Instance.Debug("Network", $"Persisted {list.Count} cookies for {uri.Host}");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warning("Network", $"Cookie persist failed: {ex.Message}");
        }
    }

    private void RestoreCookiesFromDisk()
    {
        try
        {
            if (!File.Exists(_cookiePersistPath)) return;

            string json;
            lock (_cookieLock)
            {
#if DEBUG
                json = File.ReadAllText(_cookiePersistPath);
#else
                var encrypted = File.ReadAllBytes(_cookiePersistPath);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                json = Encoding.UTF8.GetString(decrypted);
#endif
            }

            var list = JsonConvert.DeserializeObject<List<SerializedCookie>>(json);
            if (list == null) return;

            var restored = 0;
            foreach (var sc in list)
            {
                // Skip expired cookies
                if (sc.Expires != DateTime.MinValue && sc.Expires < DateTime.Now) continue;
                var cookie = new Cookie(sc.Name, sc.Value, sc.Path, sc.Domain)
                {
                    Secure = sc.Secure,
                    HttpOnly = sc.HttpOnly,
                    Expires = sc.Expires
                };
                _cookieContainer.Add(cookie);
                restored++;
            }

            FileLogger.Instance.Info("Network", $"Restored {restored} cookies from disk");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warning("Network", $"Cookie restore failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear persisted cookies (called on logout).
    /// </summary>
    public void ClearPersistedCookies()
    {
        try
        {
            lock (_cookieLock)
            {
                if (File.Exists(_cookiePersistPath))
                    File.Delete(_cookiePersistPath);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warning("Network", $"Clear persisted cookies failed: {ex.Message}");
        }
    }

    private class SerializedCookie
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Domain { get; set; } = "";
        public string Path { get; set; } = "/";
        public DateTime Expires { get; set; }
        public bool Secure { get; set; }
        public bool HttpOnly { get; set; }
    }

    private void AttachAuthHeaders(HttpRequestMessage request)
    {
        // macOS uses x-access-token header for REST APIs (NOT Authorization: Bearer)
        var token = AuthManager.Instance.Storage.UserToken;
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.TryAddWithoutValidation("x-access-token", token);
        }

        // CSRF token for state-changing methods (POST/PUT/DELETE/PATCH) — matches macOS exactly
        var csrf = AuthManager.Instance.Storage.CSRFToken;
        var method = request.Method.Method.ToUpper();
        if (!string.IsNullOrEmpty(csrf) &&
            (method == "POST" || method == "PUT" || method == "DELETE" || method == "PATCH"))
        {
            request.Headers.TryAddWithoutValidation("x-csrf-token", csrf);
        }

        var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
        if (!string.IsNullOrEmpty(workspaceId))
        {
            request.Headers.TryAddWithoutValidation("x-workspace-id", workspaceId);
        }
    }

    public async Task<T?> GetAsync<T>(string url) where T : class
    {
        return await ExecuteWithRetry<T>(HttpMethod.Get, url, null);
    }

    public async Task<T?> PostAsync<T>(string url, object? body = null) where T : class
    {
        return await ExecuteWithRetry<T>(HttpMethod.Post, url, body);
    }

    public async Task<T?> PutAsync<T>(string url, object? body = null) where T : class
    {
        return await ExecuteWithRetry<T>(HttpMethod.Put, url, body);
    }

    public async Task<T?> PatchAsync<T>(string url, object? body = null) where T : class
    {
        return await ExecuteWithRetry<T>(HttpMethod.Patch, url, body);
    }

    public async Task<bool> DeleteAsync(string url)
    {
        try
        {
            var result = await ExecuteWithRetry<object>(HttpMethod.Delete, url, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetRawAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AttachAuthHeaders(request);
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Network", $"GET raw failed: {url} - {ex.Message}");
        }
        return null;
    }

    public async Task<string?> PostRawAsync(string url, object? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            AttachAuthHeaders(request);
            if (body != null)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            }
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                FileLogger.Instance.Warning("Network",
                    $"POST raw failed: {(int)response.StatusCode} {url} - {content.Substring(0, Math.Min(200, content.Length))}");
                return null;
            }
            return content;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Network", $"POST raw failed: {url} - {ex.Message}");
            return null;
        }
    }

    public async Task<Result<string>> PostRawCheckedAsync(string url, object? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            AttachAuthHeaders(request);
            if (body != null)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            }
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return Result.Success(content);
            }
            FileLogger.Instance.Warning("Network", $"POST checked failed: {(int)response.StatusCode} {url}");
            return Result.Failure<string>($"HTTP {(int)response.StatusCode}: {content.Substring(0, Math.Min(200, content.Length))}");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Network", $"POST checked failed: {url} - {ex.Message}");
            return Result.Failure<string>(ex.Message, ex);
        }
    }

    public async Task<string?> PutRawAsync(string url, object? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            AttachAuthHeaders(request);
            if (body != null)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            }
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                FileLogger.Instance.Warning("Network",
                    $"PUT raw failed: {(int)response.StatusCode} {url} - {content.Substring(0, Math.Min(200, content.Length))}");
                return null;
            }
            return content;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Network", $"PUT raw failed: {url} - {ex.Message}");
            return null;
        }
    }

    public async Task<string?> DeleteRawAsync(string url, object? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            AttachAuthHeaders(request);
            if (body != null)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            }
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                FileLogger.Instance.Warning("Network",
                    $"DELETE raw failed: {(int)response.StatusCode} {url} - {content.Substring(0, Math.Min(200, content.Length))}");
                return null;
            }
            return content;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Network", $"DELETE raw failed: {url} - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Core request execution with 401/406 auto-retry.
    /// Matches macOS: on 401 "jwt expired" or 406 "Not Approved", refresh token and retry once.
    /// On 5xx, log to error service. No retry on other 4xx codes.
    /// </summary>
    private async Task<T?> ExecuteWithRetry<T>(HttpMethod method, string url, object? body) where T : class
    {
        var (result, statusCode, responseBody) = await ExecuteRequest<T>(method, url, body);
        if (result != null || statusCode == 0) return result;

        // Check for retryable status codes (matches macOS exactly)
        bool shouldRetry = false;
        if (statusCode == 401)
        {
            var message = ExtractMessage(responseBody);
            shouldRetry = message == "jwt expired";
            if (shouldRetry)
                FileLogger.Instance.Info("Network", "401 jwt expired — refreshing token");
        }
        else if (statusCode == 406)
        {
            var message = ExtractMessage(responseBody);
            shouldRetry = message == "Not Approved";
            if (shouldRetry)
                FileLogger.Instance.Info("Network", "406 Not Approved — refreshing token");
        }
        else if (statusCode >= 500)
        {
            // Log 5xx server errors (matches macOS)
            ErrorService.Instance.LogMessage(
                $"HTTP {statusCode} server error",
                ErrorCategory.Network, ErrorSeverity.Error,
                new Dictionary<string, string>
                {
                    ["url"] = url,
                    ["method"] = method.Method,
                    ["status_code"] = statusCode.ToString()
                });
        }

        if (!shouldRetry) return result;

        // Refresh token via guarded method, then retry once
        var refreshed = await TokenRefreshService.Instance.EnsureTokenRefreshed();
        if (!refreshed)
        {
            FileLogger.Instance.Warning("Network", "Token refresh failed — logging out");
            AuthManager.Instance.Logout();
            return default;
        }

        // Retry with new token
        var (retryResult, _, _) = await ExecuteRequest<T>(method, url, body);
        return retryResult;
    }

    private async Task<(T? result, int statusCode, string? responseBody)> ExecuteRequest<T>(
        HttpMethod method, string url, object? body) where T : class
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            AttachAuthHeaders(request);
            if (body != null)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            }

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrEmpty(content)) return (default, statusCode, content);
                var parsed = JsonConvert.DeserializeObject<T>(content);
                return (parsed, statusCode, content);
            }

            FileLogger.Instance.Warning("Network",
                $"Request failed: {statusCode} {method} {url} - {content.Substring(0, Math.Min(200, content.Length))}");
            return (default, statusCode, content);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Network", $"{method} {url} failed: {ex.Message}");
            return (default, 0, null);
        }
    }

    /// <summary>
    /// Extract "message" field from JSON response body for 401/406 handling.
    /// </summary>
    private static string ExtractMessage(string? responseBody)
    {
        if (string.IsNullOrEmpty(responseBody)) return "";
        try
        {
            var json = JObject.Parse(responseBody);
            return json["message"]?.ToString() ?? "";
        }
        catch { return ""; }
    }
}
