namespace MasjidApp.Core.Caching;

/// <summary>
/// Coordinates durable cache resources with application lifecycle transitions.
/// </summary>
public interface ICacheStoreLifecycle
{
    /// <summary>
    /// Waits for active cache operations and closes resources that must not remain locked
    /// while the application is suspended. The next cache operation reopens them lazily.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);
}
