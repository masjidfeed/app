using MasjidApp.Core.Http;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Services;

// API-06, API-10, FR-ST-08: the central seam all feature batches use to access
// the active masjid's config, branding, and feature flags.

/// <summary>
/// Provides the active masjid's configuration to all feature batches.
/// Subscribe to <see cref="ActiveMasjidChanged"/> to react to masjid switches.
/// </summary>
public interface IMasjidContext
{
    /// <summary>
    /// The currently active masjid entry (identity + config URL).
    /// <c>null</c> until a masjid has been added and selected.
    /// </summary>
    MasjidStoredEntry? ActiveEntry { get; }

    /// <summary>
    /// Fetches (or returns cached) the full config document for the active masjid.
    /// Returns <c>null</c> if no masjid is active or the fetch fails entirely.
    /// </summary>
    Task<ApiResult<MasjidConfig>?> GetConfigAsync(
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);

    /// <summary>
    /// Sets the active masjid to <paramref name="entry"/> and persists the choice.
    /// Raises <see cref="ActiveMasjidChanged"/>.
    /// </summary>
    void SetActive(MasjidStoredEntry entry);

    /// <summary>
    /// Raised on the calling thread when the active masjid is changed.
    /// UI layers should rebind branding / navigation on receipt.
    /// </summary>
    event EventHandler ActiveMasjidChanged;
}
