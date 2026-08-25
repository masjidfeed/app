namespace MasjidApp.Services;

/// <inheritdoc />
public sealed class NavigationService : INavigationService
{
    /// <inheritdoc />
    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null)
    {
        // Shell navigation must happen on the main thread.
        if (MainThread.IsMainThread)
        {
            var shell = CurrentShell();
            return parameters is null ? shell.GoToAsync(route) : shell.GoToAsync(route, parameters);
        }

        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var shell = CurrentShell();
            return parameters is null ? shell.GoToAsync(route) : shell.GoToAsync(route, parameters);
        });
    }

    /// <inheritdoc />
    public Task GoBackAsync()
    {
        if (MainThread.IsMainThread)
        {
            return CurrentShell().GoToAsync("..");
        }

        return MainThread.InvokeOnMainThreadAsync(() => CurrentShell().GoToAsync(".."));
    }

    private static Shell CurrentShell() =>
        Shell.Current ?? throw new InvalidOperationException("Shell has not been initialized yet.");
}
