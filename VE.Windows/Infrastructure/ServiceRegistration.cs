using Microsoft.Extensions.DependencyInjection;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Services;

namespace VE.Windows.Infrastructure;

/// <summary>
/// Composition root: registers all services in the DI container.
/// Existing code continues to work via static Instance properties;
/// new code should resolve via IServiceProvider or constructor injection.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddVEServices(this IServiceCollection services)
    {
        // Infrastructure
        services.AddSingleton<IAppConfiguration>(AppConfiguration.Instance);
        services.AddSingleton<IFileLogger>(FileLogger.Instance);

        // Core services (register existing singletons)
        services.AddSingleton<IErrorService>(ErrorService.Instance);
        services.AddSingleton<INetworkService>(NetworkService.Instance);
        services.AddSingleton<IAuthManager>(AuthManager.Instance);
        services.AddSingleton<IBaseURLService>(BaseURLService.Instance);
        services.AddSingleton<IWebSocketRegistry>(WebSocket.WebSocketRegistry.Instance);
        services.AddSingleton<ITokenRefreshService>(TokenRefreshService.Instance);

        // Managers
        services.AddSingleton<IAudioService>(AudioService.Instance);
        services.AddSingleton<IScreenCaptureManager>(ScreenCaptureManager.Instance);
        services.AddSingleton<IClipboardManager>(ClipboardManager.Instance);
        services.AddSingleton<IViewCoordinator>(ViewCoordinator.Instance);
        services.AddSingleton<ISettingsManager>(Models.SettingsManager.Instance);

        // Feature services
        services.AddSingleton<IDictationService>(DictationService.Instance);
        services.AddSingleton<IMeetingService>(MeetingService.Instance);

        return services;
    }
}
