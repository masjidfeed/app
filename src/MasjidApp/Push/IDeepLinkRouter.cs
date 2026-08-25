namespace MasjidApp.Push;

/// <summary>Routes versioned custom app links after the application Shell is ready.</summary>
public interface IDeepLinkRouter
{
    /// <summary>Routes a custom URI, or queues it while the Shell is starting.</summary>
    Task<bool> RouteAsync(Uri uri, CancellationToken ct = default);

    /// <summary>Routes the most recent link queued during cold start.</summary>
    Task RoutePendingAsync(CancellationToken ct = default);
}