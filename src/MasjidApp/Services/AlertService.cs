namespace MasjidApp.Services;

/// <inheritdoc />
public sealed class AlertService : IAlertService
{
    /// <inheritdoc />
    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
        // Alerts must be shown on the main thread (same pattern as NavigationService).
        if (MainThread.IsMainThread)
        {
            return ShowCoreAsync(title, message, accept, cancel);
        }

        return MainThread.InvokeOnMainThreadAsync(
            () => ShowCoreAsync(title, message, accept, cancel));
    }

    private static Task<bool> ShowCoreAsync(string title, string message, string accept, string cancel) =>
        Shell.Current is { } shell
            ? shell.DisplayAlertAsync(title, message, accept, cancel)
            : Task.FromResult(false);
}
