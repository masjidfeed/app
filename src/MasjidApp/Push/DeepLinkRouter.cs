using MasjidApp.Core.Push;
using MasjidApp.ViewModels;

namespace MasjidApp.Push;

/// <inheritdoc />
public sealed class DeepLinkRouter : IDeepLinkRouter
{
    private readonly HomeShellViewModel _home;
    private readonly Lock _sync = new();
    private PostDeepLink? _pending;

    public DeepLinkRouter(HomeShellViewModel home)
    {
        _home = home;
    }

    /// <inheritdoc />
    public Task<bool> RouteAsync(Uri uri, CancellationToken ct = default)
    {
        if (!PostDeepLink.TryParse(uri, out var deepLink) || deepLink is null)
        {
            return Task.FromResult(false);
        }

        if (Shell.Current is null)
        {
            lock (_sync)
            {
                _pending = deepLink;
            }

            return Task.FromResult(true);
        }

        return MainThread.InvokeOnMainThreadAsync(() => _home.OpenPostAsync(deepLink, ct));
    }

    /// <inheritdoc />
    public async Task RoutePendingAsync(CancellationToken ct = default)
    {
        PostDeepLink? deepLink;
        lock (_sync)
        {
            deepLink = _pending;
            _pending = null;
        }

        if (deepLink is not null)
        {
            await MainThread.InvokeOnMainThreadAsync(() => _home.OpenPostAsync(deepLink, ct));
        }
    }
}