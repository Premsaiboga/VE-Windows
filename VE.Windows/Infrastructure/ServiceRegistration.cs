using Microsoft.Extensions.DependencyInjection;
using VE.Windows.Helpers;
using VE.Windows.Managers;
using VE.Windows.Services;

namespace VE.Windows.Infrastructure;

/// <summary>
/// Composition root: registers all services in the DI container.
/// Existing code continues to work via static Instance properties;
/// new code should resolve via IServiceProvider or constructor injection.
/// Uses factory lambdas since singletons don't yet declare interface implementations.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddVEServices(this IServiceCollection services)
    {
        // Infrastructure
        services.AddSingleton<IAppConfiguration>(_ => AppConfiguration.Instance);
        services.AddSingleton<IFileLogger>(_ => FileLogger.Instance);

        // Core services (register existing singletons)
        services.AddSingleton<IErrorService>(_ => ErrorService.Instance);
        services.AddSingleton<INetworkService>(_ => NetworkService.Instance);
        services.AddSingleton<IAuthManager>(_ => AuthManager.Instance);
        services.AddSingleton<IBaseURLService>(_ => BaseURLService.Instance);
        services.AddSingleton<IWebSocketRegistry>(_ => WebSocket.WebSocketRegistry.Instance);
        services.AddSingleton<ITokenRefreshService>(_ => TokenRefreshService.Instance);

        // Managers
        services.AddSingleton<IAudioService>(_ => AudioService.Instance);
        services.AddSingleton<IScreenCaptureManager>(_ => ScreenCaptureManager.Instance);
        services.AddSingleton<IClipboardManager>(_ => ClipboardManager.Instance);
        services.AddSingleton<IViewCoordinator>(_ => ViewCoordinator.Instance);
        services.AddSingleton<ISettingsManager>(_ => Models.SettingsManager.Instance);

        // Feature services
        services.AddSingleton<IDictationService>(_ => DictationService.Instance);
        services.AddSingleton<IMeetingService>(_ => MeetingService.Instance);

        return services;
    }
}
