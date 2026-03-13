using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using VE.Windows.Helpers;
using VE.Windows.Managers;

namespace VE.Windows.Services;

/// <summary>
/// HTTP networking layer with auth headers, CSRF tokens, cookie handling, and retry logic.
/// Equivalent to macOS NetworkService.
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

    private void AttachAuthHeaders(HttpRequestMessage request)
    {
        var token = AuthManager.Instance.Storage.UserToken;
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var csrf = AuthManager.Instance.Storage.CSRFToken;
        if (!string.IsNullOrEmpty(csrf))
        {
            request.Headers.Add("x-csrf-token", csrf);
        }

        var workspaceId = AuthManager.Instance.Storage.WorkspaceId;
        if (!string.IsNullOrEmpty(workspaceId))
        {
            request.Headers.Add("x-workspace-id", workspaceId);
        }
    }

    public async Task<T?> GetAsync<T>(string url) where T : class
    {
        return await ExecuteWithRetry<T>(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AttachAuthHeaders(request);
            var response = await _httpClient.SendAsync(request);
            return await HandleResponse<T>(response);
        });
    }

    public async Task<T?> PostAsync<T>(string url, object? body = null) where T : class
    {
        return await ExecuteWithRetry<T>(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            AttachAuthHeaders(request);
            if (body != null)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            }
            var response = await _httpClient.SendAsync(request);
            return await HandleResponse<T>(response);
        });
    }

    public async Task<T?> PutAsync<T>(string url, object? body = null) where T : class
    {
        return await ExecuteWithRetry<T>(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            AttachAuthHeaders(request);
            if (body != null)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            }
            var response = await _httpClient.SendAsync(request);
            return await HandleResponse<T>(response);
        });
    }

    public async Task<T?> PatchAsync<T>(string url, object? body = null) where T : class
    {
        return await ExecuteWithRetry<T>(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, url);
            AttachAuthHeaders(request);
            if (body != null)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            }
            var response = await _httpClient.SendAsync(request);
            return await HandleResponse<T>(response);
        });
    }

    public async Task<bool> DeleteAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            AttachAuthHeaders(request);
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Network", $"DELETE failed: {url} - {ex.Message}");
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

    private async Task<T?> HandleResponse<T>(HttpResponseMessage response) where T : class
    {
        var content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrEmpty(content)) return default;
            return JsonConvert.DeserializeObject<T>(content);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("Token expired");
        }

        FileLogger.Instance.Warning("Network",
            $"Request failed: {(int)response.StatusCode} {response.RequestMessage?.RequestUri} - {content}");
        return default;
    }

    private async Task<T?> ExecuteWithRetry<T>(Func<Task<T?>> action) where T : class
    {
        try
        {
            return await action();
        }
        catch (UnauthorizedAccessException)
        {
            // Token expired - try refresh
            FileLogger.Instance.Info("Network", "Token expired, attempting refresh...");
            var refreshed = await TokenRefreshService.Instance.RefreshToken();
            if (refreshed)
            {
                try { return await action(); }
                catch (Exception ex)
                {
                    FileLogger.Instance.Error("Network", $"Retry after refresh failed: {ex.Message}");
                    return default;
                }
            }
            // Refresh failed - logout
            AuthManager.Instance.Logout();
            return default;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Network", $"Request failed: {ex.Message}");
            return default;
        }
    }
}
