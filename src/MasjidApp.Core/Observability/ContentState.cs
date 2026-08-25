using MasjidApp.Core.Http;

namespace MasjidApp.Core.Observability;

// NFR-OF-02, NFR-OF-03: shared model exposing cache freshness to every content screen.

/// <summary>Represents the freshness state of a content screen.</summary>
public sealed record ContentState(
    bool IsOffline,
    DateTimeOffset? LastUpdated,
    bool IsStale)
{
    /// <summary>Default threshold after which cached content is considered stale (NFR-OF-02).</summary>
    public static readonly TimeSpan DefaultStaleThreshold = TimeSpan.FromHours(24);

    /// <summary>
    /// Constructs a <see cref="ContentState"/> from an <see cref="ApiResult{T}"/>.
    /// </summary>
    /// <typeparam name="T">The result data type.</typeparam>
    /// <param name="result">The API result to derive state from.</param>
    /// <param name="staleThreshold">Override threshold; defaults to <see cref="DefaultStaleThreshold"/>.</param>
    public static ContentState FromApiResult<T>(ApiResult<T> result, TimeSpan? staleThreshold = null)
        where T : class
    {
        var threshold = staleThreshold ?? DefaultStaleThreshold;
        var fetchedAt = result.FetchedAtOrNull;
        bool isStale = fetchedAt.HasValue && (DateTimeOffset.UtcNow - fetchedAt.Value) > threshold;
        bool isOffline = result is ApiResult<T>.Stale or ApiResult<T>.Failed;
        return new ContentState(isOffline, fetchedAt, isStale);
    }

    /// <summary>
    /// Combines the freshness of several independently loaded content sources.
    /// The aggregate is degraded when any source is offline or stale, and reports
    /// the oldest source timestamp because that bounds the screen's freshness.
    /// </summary>
    /// <param name="states">Content states to combine.</param>
    /// <returns>The aggregate freshness state.</returns>
    public static ContentState Combine(params ContentState[] states)
    {
        ArgumentNullException.ThrowIfNull(states);

        DateTimeOffset? oldestUpdate = null;
        var isOffline = false;
        var isStale = false;

        foreach (var state in states)
        {
            isOffline |= state.IsOffline;
            isStale |= state.IsStale;
            if (state.LastUpdated is { } updated &&
                (!oldestUpdate.HasValue || updated < oldestUpdate.Value))
            {
                oldestUpdate = updated;
            }
        }

        return new ContentState(isOffline, oldestUpdate, isStale);
    }
}
