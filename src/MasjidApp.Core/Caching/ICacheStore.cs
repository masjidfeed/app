namespace MasjidApp.Core.Caching;

// API-08, NFR-OF-03: local store abstraction; backed by SQLite in production.

/// <summary>
/// Key-value store for HTTP response bodies and their validation metadata.
/// Persists across app restarts so the app can serve cached content fully offline.
/// </summary>
public interface ICacheStore
{
    /// <summary>
    /// Returns the cached entry for <paramref name="key"/>, or <c>null</c> if absent.
    /// </summary>
    Task<Http.CacheEntry?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces the cache entry for <paramref name="entry.Key"/>.
    /// </summary>
    Task SetAsync(Http.CacheEntry entry, CancellationToken ct = default);
}
