using MasjidApp.Core.Http;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Announcements;

// FR-CM-01, API-05b

/// <summary>
/// Fetches and sanitizes masjid announcements from the Masjid API.
/// </summary>
public interface IAnnouncementsService
{
    /// <summary>
    /// Fetches the announcements list from <paramref name="announcementsUrl"/>,
    /// using the local cache for conditional requests and offline fallback.
    /// </summary>
    /// <param name="announcementsUrl">Absolute URL of the announcements endpoint.</param>
    /// <param name="cachePolicy">Whether a fresh cached payload may bypass the network.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="ApiResult{T}"/> wrapping the parsed announcement list.
    /// Never throws; all failure modes are encoded in the return type.
    /// </returns>
    Task<ApiResult<List<MasjidPost>>> GetAnnouncementsAsync(
        string announcementsUrl,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the sanitized HTML content of <paramref name="announcement"/>,
    /// safe to render (NFR-SC-02). Returns an empty string when
    /// <see cref="Models.MasjidPost.Description"/> is absent.
    /// </summary>
    string GetSanitizedContent(MasjidPost announcement);
}
