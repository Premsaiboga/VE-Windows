using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using VE.Windows.Helpers;
using VE.Windows.Models;
using VE.Windows.Services;
using VE.Windows.Infrastructure;
using VE.Windows.WebSocket;

namespace VE.Windows.Managers;

/// <summary>
/// Main authentication manager - handles OAuth flow, token storage, and session management.
/// Equivalent to macOS AuthManager.
/// </summary>
public sealed class AuthManager : INotifyPropertyChanged, IAuthManager
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

    public async Task HandleOAuthCallback(string sessionId)
    {
        if (_isProcessingCallback) return;
        _isProcessingCallback = true;

        try
        {
            FileLogger.Instance.Info("AuthManager", $"Processing OAuth callback with sessionId: {sessionId.Substring(0, Math.Min(8, sessionId.Length))}...");
            var baseUrl = BaseURLService.Instance.GetGlobalUrl("auth");
            var response = await NetworkService.Instance.PostRawAsync(
                $"{baseUrl}/exchange-one-time-token",
                new { sessionId });

            if (response == null)
            {
                FileLogger.Instance.Error("AuthManager", "Token exchange returned null response");
                AuthState = AuthState.Error;
                return;
            }

            FileLogger.Instance.Info("AuthManager", $"Token exchange response received ({response.Length} chars)");

            var result = JsonConvert.DeserializeObject<TokenExchangeResponse>(response);
            if (result?.AccessToken == null)
            {
                FileLogger.Instance.Error("AuthManager", "No accessToken in exchange response");
                AuthState = AuthState.Error;
                return;
            }

            // Store tokens
            Storage.UserToken = result.AccessToken;
            Storage.CSRFToken = result.CsrfToken;

            // Extract workspace info from accessibleWorkspaces
            if (result.AccessibleWorkspaces != null && result.AccessibleWorkspaces.Length > 0)
            {
                var workspace = result.AccessibleWorkspaces[0];
                Storage.WorkspaceId = workspace.WorkspaceId;
                Storage.Region = workspace.Region ?? result.Region ?? "us-east-1";
                Storage.WorkspaceMode = workspace.WorkspaceMode;
                Storage.TenantId = workspace.TenantId ?? workspace.Tenant_id;
                if (workspace.IsOnboard.HasValue)
                    Storage.IsOnboard = workspace.IsOnboard.Value;
            }
            else
            {
                Storage.Region = result.Region ?? "us-east-1";
            }

            // Persist refresh token cookie to disk (matches macOS HTTPCookieStorage.shared auto-persist)
            var authUri = new Uri(baseUrl);
            NetworkService.Instance.PersistCookies(authUri);

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
        NetworkService.Instance.ClearPersistedCookies();
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
                Storage.UserName = profile.Name;
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
                Storage.TenantName = tenant.Name;
                Storage.TenantPlan = tenant.Plan;
                Storage.TenantEmail = tenant.Email;
                Storage.TenantWebsite = tenant.Website;
                Storage.TenantCompanyType = tenant.CompanyType;
                Storage.TenantPhone = tenant.Phone;
                Storage.TenantAddress = tenant.Address;
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
        [JsonProperty("region")] public string? Region { get; set; }
        [JsonProperty("accessibleWorkspaces")] public WorkspaceInfo[]? AccessibleWorkspaces { get; set; }
        [JsonProperty("accessTokenExpiry")] public object? AccessTokenExpiry { get; set; }
        [JsonProperty("refreshTokenExpiry")] public object? RefreshTokenExpiry { get; set; }
    }

    private class WorkspaceInfo
    {
        [JsonProperty("workspaceId")] public string? WorkspaceId { get; set; }
        [JsonProperty("region")] public string? Region { get; set; }
        [JsonProperty("workspaceMode")] public string? WorkspaceMode { get; set; }
        [JsonProperty("tenantId")] public string? TenantId { get; set; }
        [JsonProperty("tenant_id")] public string? Tenant_id { get; set; }
        [JsonProperty("isOnboard")] public bool? IsOnboard { get; set; }
    }
}

/// <summary>
/// Secure credential storage with DEBUG/RELEASE split.
/// DEBUG: plain JSON file (survives rebuilds, easy to inspect).
/// RELEASE: Windows DPAPI (ProtectedData.Protect/Unprotect) for encrypted storage.
/// Matches macOS AuthStorage: DEBUG uses UserDefaults, RELEASE uses Keychain.
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
#if DEBUG
        _storagePath = Path.Combine(veDir, "auth_debug.json");
#else
        _storagePath = Path.Combine(veDir, "auth.dat");
#endif
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
    public string? UserName { get => Get("userName"); set => Set("userName", value); }
    public string? TenantName { get => Get("tenantName"); set => Set("tenantName", value); }
    public string? TenantPlan { get => Get("tenantPlan"); set => Set("tenantPlan", value); }
    public int TenantMemberCount
    {
        get => int.TryParse(Get("tenantMemberCount"), out var v) ? v : 0;
        set => Set("tenantMemberCount", value.ToString());
    }
    public string? WorkspaceMode { get => Get("workspaceMode"); set => Set("workspaceMode", value); }
    public string? TenantEmail { get => Get("tenantEmail"); set => Set("tenantEmail", value); }
    public string? TenantWebsite { get => Get("tenantWebsite"); set => Set("tenantWebsite", value); }
    public string? TenantCompanyType { get => Get("tenantCompanyType"); set => Set("tenantCompanyType", value); }
    public string? TenantPhone { get => Get("tenantPhone"); set => Set("tenantPhone", value); }
    public string? TenantAddress { get => Get("tenantAddress"); set => Set("tenantAddress", value); }
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
#if DEBUG
                // DEBUG: plain JSON file for easy development
                var json = File.ReadAllText(_storagePath);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
#else
                // RELEASE: DPAPI-encrypted storage
                var encrypted = File.ReadAllBytes(_storagePath);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
#endif
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Warning("AuthManager", $"LoadStore failed: {ex.Message}");
        }
        return new Dictionary<string, string>();
    }

    private void SaveStore()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_store);
#if DEBUG
            // DEBUG: plain JSON file
            File.WriteAllText(_storagePath, json);
#else
            // RELEASE: DPAPI-encrypted storage
            var bytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_storagePath, encrypted);
#endif
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("AuthStorage", $"Save failed: {ex.Message}");
        }
    }
}
