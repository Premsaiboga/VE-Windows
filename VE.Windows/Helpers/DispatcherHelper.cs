using System.Windows;
using System.Windows.Threading;

namespace VE.Windows.Helpers;

/// <summary>
/// Thread-safe dispatcher helper for WPF UI operations.
/// Centralizes Dispatcher access to avoid null-reference crashes when Application.Current is null
/// (e.g., during shutdown or unit tests).
/// Equivalent to macOS @MainActor / DispatchQueue.main.
/// </summary>
public static class DispatcherHelper
{
    /// <summary>
    /// Execute an action on the UI thread. If already on the UI thread, executes synchronously.
    /// Safe to call from any thread. No-op if Application.Current is null (shutdown/test).
    /// </summary>
    public static void RunOnUI(Action action)
    {
        var dispatcher = GetDispatcher();
        if (dispatcher == null) return;

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    /// <summary>
    /// Post an action to the UI thread asynchronously. Does not block the caller.
    /// Safe to call from any thread. No-op if Application.Current is null.
    /// </summary>
    public static void PostOnUI(Action action)
    {
        var dispatcher = GetDispatcher();
        dispatcher?.BeginInvoke(action);
    }

    /// <summary>
    /// Execute an async action on the UI thread and return a Task.
    /// Safe to call from any thread.
    /// </summary>
    public static async Task RunOnUIAsync(Func<Task> asyncAction)
    {
        var dispatcher = GetDispatcher();
        if (dispatcher == null) return;

        if (dispatcher.CheckAccess())
        {
            await asyncAction();
        }
        else
        {
            await dispatcher.InvokeAsync(asyncAction).Task.Unwrap();
        }
    }

    /// <summary>
    /// Execute a function on the UI thread and return its result.
    /// </summary>
    public static T? RunOnUI<T>(Func<T> func)
    {
        var dispatcher = GetDispatcher();
        if (dispatcher == null) return default;

        if (dispatcher.CheckAccess())
        {
            return func();
        }
        else
        {
            return dispatcher.Invoke(func);
        }
    }

    private static Dispatcher? GetDispatcher()
    {
        try
        {
            return Application.Current?.Dispatcher;
        }
        catch
        {
            return null;
        }
    }
}
