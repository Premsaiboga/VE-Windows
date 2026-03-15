using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VE.Windows.Helpers;
using VE.Windows.Managers;

namespace VE.Windows.Services;

/// <summary>
/// HTTP networking layer with auth headers, CSRF tokens, HTTPOnly cookie handling,
/// and automatic 401/406 retry logic.
/// Matches macOS NetworkService exactly: x-access-token header, x-csrf-token for
/// state-changing methods, cookie-based refresh token flow, single retry on token expiry.
/// </summary>
public sealed class NetworkService
{
    public static NetworkService Instance { get; } = new();

    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;

    private NetworkService()
    {
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            // Accept HTTPOnly cookies from the server (refresh token flow)
            // CookieContainer automatically handles HTTPOnly cookies sent by the server
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
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Network", $"POST raw failed: {url} - {ex.Message}");
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
