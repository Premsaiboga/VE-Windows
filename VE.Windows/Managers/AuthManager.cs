using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;
using VE.Windows.WebSocket;

namespace VE.Windows.Managers;

/// <summary>
/// Main authentication manager - handles OAuth flow, token storage, and session management.
/// Equivalent to macOS AuthManager.
/// </summary>
public sealed class AuthManager : INotifyPropertyChanged
{
    public static AuthManager Instance { get; } = new();

    private AuthState _authState = AuthState.Unauthorized;
    private string? _workspaceMode;
    private bool _isProcessingCallback;
    private bool _isPostAuthAPIsCalled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AuthStorage Storage { get; } = new();

    public AuthState AuthState
    {
        get => _authState;
        set { _authState = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAuthenticated)); }
    }

    public bool IsAuthenticated => _authState == AuthState.Authorized;

    public string? WorkspaceMode
    {
        get => _workspaceMode;
        set { _workspaceMode = value; OnPropertyChanged(); }
    }

    private AuthManager()
    {
        CheckAuthState();
    }

    public void CheckAuthState()
    {
        WorkspaceMode = Storage.WorkspaceMode;
        if (Storage.IsAuthenticated)
        {
            AuthState = AuthState.Authorized;
            ErrorService.Instance.UpdateSentryUser();
            RunPostAuthAPIsIfNeeded();
        }
        else
        {
            AuthState = AuthState.Unauthorized;
        }
    }

    private async void RunPostAuthAPIsIfNeeded()
    {
        if (_isPostAuthAPIsCalled) return;
        _isPostAuthAPIsCalled = true;

        await Task.Delay(100);
        if (Storage.UserToken == null)
        {
            _isPostAuthAPIsCalled = false;
            return;
        }

        var tasks = new List<Task>
        {
            FetchUserProfile(),
            FetchTenantInfo(),
            PreWarmServices()
        };

        await Task.WhenAll(tasks);
    }

    private async Task PreWarmServices()
    {
        FileLogger.Instance.Info("AuthManager", "Pre-warming services...");
        await WebSocketRegistry.Instance.ConnectUnifiedAudioTransport();
        FileLogger.Instance.Info("AuthManager", "Services pre-warmed");
    }

    public async Task AuthenticateWithOutlook()
    {
        if (ViewCoordinator.Instance.RestrictedState == RestrictedState.NoInternet) return;

        AuthState = AuthState.Authenticating;
        var redirectUri = "ve://oauth/callback";
        var baseUrl = BaseURLService.Instance.GetGlobalUrl("auth");
        var authUrl = $"{baseUrl}/microsoft/url?redirectUri={Uri.EscapeDataString(redirectUri)}&isApp=true";

        FileLogger.Instance.Info("AuthManager", $"Opening Outlook auth URL: {authUrl}");
        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
    }

    public async Task AuthenticateWithGoogle()
    {
        if (ViewCoordinator.Instance.RestrictedState == RestrictedState.NoInternet) return;

        AuthState = AuthState.Authenticating;
        var redirectUri = "ve://oauth/callback";
        var baseUrl = BaseURLService.Instance.GetGlobalUrl("auth");
        var authUrl = $"{baseUrl}/google/url?redirectUri={Uri.EscapeDataString(redirectUri)}&isApp=true";

        FileLogger.Instance.Info("AuthManager", $"Opening Google auth URL: {authUrl}");
        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
    }

    public async Task HandleOAuthCallback(string code, string? state = null)
    {
        if (_isProcessingCallback) return;
        _isProcessingCallback = true;

        try
        {
            FileLogger.Instance.Info("AuthManager", "Processing OAuth callback...");
            var baseUrl = BaseURLService.Instance.GetGlobalUrl("auth");
            var response = await NetworkService.Instance.PostRawAsync(
                $"{baseUrl}/token/exchange",
                new { code, redirectUri = "ve://oauth/callback" });

            if (response == null)
            {
                AuthState = AuthState.Error;
                return;
            }

            var result = JsonConvert.DeserializeObject<TokenExchangeResponse>(response);
            if (result?.AccessToken == null)
            {
                AuthState = AuthState.Error;
                return;
            }

            // Store tokens
            Storage.UserToken = result.AccessToken;
            Storage.CSRFToken = result.CsrfToken;
            Storage.WorkspaceId = result.WorkspaceId;
            Storage.Region = result.Region;

            // Schedule token refresh
            TokenRefreshService.Instance.ScheduleRefresh(result.AccessToken);

            AuthState = AuthState.Authorized;
            ErrorService.Instance.UpdateSentryUser();

            _isPostAuthAPIsCalled = false;
            RunPostAuthAPIsIfNeeded();

            FileLogger.Instance.Info("AuthManager", "Authentication successful");
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("AuthManager", $"OAuth callback failed: {ex.Message}");
            AuthState = AuthState.Error;
        }
        finally
        {
            _isProcessingCallback = false;
        }
    }

    public void Logout()
    {
        FileLogger.Instance.Info("AuthManager", "Logging out...");
        Storage.ClearAll();
        AuthState = AuthState.Unauthorized;
        _isPostAuthAPIsCalled = false;
        WorkspaceMode = null;
        WebSocketRegistry.Instance.DisconnectAll();
    }

    private async Task FetchUserProfile()
    {
        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("tenant-users");
            if (baseUrl == null) return;
            var profile = await NetworkService.Instance.GetAsync<UserProfile>($"{baseUrl}/me");
            if (profile != null)
            {
                Storage.UserEmail = profile.Email;
                Storage.TenantId = profile.Id;
                Storage.IsOnboard = profile.IsOnboard;
                if (profile.Region != null) Storage.Region = profile.Region;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("AuthManager", $"Fetch profile failed: {ex.Message}");
        }
    }

    private async Task FetchTenantInfo()
    {
        try
        {
            var baseUrl = BaseURLService.Instance.GetBaseUrl("tenant");
            if (baseUrl == null) return;
            var tenant = await NetworkService.Instance.GetAsync<TenantInfo>($"{baseUrl}/info");
            if (tenant != null)
            {
                Storage.TenantId = tenant.Id;
                WorkspaceMode = tenant.WorkspaceMode;
                Storage.WorkspaceMode = tenant.WorkspaceMode;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("AuthManager", $"Fetch tenant failed: {ex.Message}");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private class TokenExchangeResponse
    {
        [JsonProperty("accessToken")] public string? AccessToken { get; set; }
        [JsonProperty("csrfToken")] public string? CsrfToken { get; set; }
        [JsonProperty("workspaceId")] public string? WorkspaceId { get; set; }
        [JsonProperty("region")] public string? Region { get; set; }
    }
}

/// <summary>
/// Secure credential storage using DPAPI encryption.
/// Equivalent to macOS AuthStorage + KeychainService.
/// </summary>
public sealed class AuthStorage
{
    private readonly string _storagePath;
    private Dictionary<string, string> _store;
    private readonly object _lock = new();

    public AuthStorage()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var veDir = Path.Combine(appData, "VE");
        Directory.CreateDirectory(veDir);
        _storagePath = Path.Combine(veDir, "auth.dat");
        _store = LoadStore();
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(UserToken) && IsTokenValid;

    public bool IsTokenValid => TokenRefreshService.IsTokenValid(UserToken);

    public string? UserToken { get => Get("userToken"); set => Set("userToken", value); }
    public string? CSRFToken { get => Get("csrfToken"); set => Set("csrfToken", value); }
    public string? WorkspaceId { get => Get("workspaceId"); set => Set("workspaceId", value); }
    public string? TenantId { get => Get("tenantId"); set => Set("tenantId", value); }
    public string? Region { get => Get("region"); set => Set("region", value); }
    public string? UserEmail { get => Get("userEmail"); set => Set("userEmail", value); }
    public string? WorkspaceMode { get => Get("workspaceMode"); set => Set("workspaceMode", value); }
    public bool IsOnboard
    {
        get => Get("isOnboard") == "true";
        set => Set("isOnboard", value ? "true" : "false");
    }

    private string? Get(string key)
    {
        lock (_lock) { return _store.GetValueOrDefault(key); }
    }

    private void Set(string key, string? value)
    {
        lock (_lock)
        {
            if (value == null) _store.Remove(key);
            else _store[key] = value;
            SaveStore();
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _store.Clear();
            SaveStore();
        }
    }

    private Dictionary<string, string> LoadStore()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                var encrypted = File.ReadAllBytes(_storagePath);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
        }
        catch { }
        return new Dictionary<string, string>();
    }

    private void SaveStore()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_store);
            var bytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_storagePath, encrypted);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("AuthStorage", $"Save failed: {ex.Message}");
        }
    }
}
