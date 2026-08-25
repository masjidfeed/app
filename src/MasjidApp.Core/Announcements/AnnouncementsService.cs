using MasjidApp.Core.Configuration;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;
using MasjidApp.Core.Utils;

namespace MasjidApp.Core.Announcements;

// FR-CM-01, API-05b, API-10, NFR-SC-02, NFR-OF-01

/// <summary>
/// Fetches, caches, and sanitizes masjid announcements.
/// All HTML content is sanitized via <see cref="HtmlSanitizer"/>
/// before being returned to callers (NFR-SC-02).
/// </summary>
public sealed class AnnouncementsService : IAnnouncementsService
{
    private readonly IMasjidApiClient _client;

    /// <summary>Initializes a new instance with the given API client.</summary>
    public AnnouncementsService(IMasjidApiClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<ApiResult<List<MasjidPost>>> GetAnnouncementsAsync(
        string announcementsUrl,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        var result = await _client
            .GetAsync(announcementsUrl, AnnouncementsJsonContext.Default.ListMasjidPost, cachePolicy, ct)
            .ConfigureAwait(false);

        // API-10: each announcement's image may be supplied relative to the masjid's
        // base URL; resolve it against the (absolute) endpoint URL so it loads correctly.
        return result switch
        {
            ApiResult<List<MasjidPost>>.Fresh f =>
                f with { Value = Normalize(f.Value, announcementsUrl) },
            ApiResult<List<MasjidPost>>.Cached c =>
                c with { Value = Normalize(c.Value, announcementsUrl) },
            ApiResult<List<MasjidPost>>.NotModified nm =>
                nm with { Value = Normalize(nm.Value, announcementsUrl) },
            ApiResult<List<MasjidPost>>.Stale s =>
                s with { Value = Normalize(s.Value, announcementsUrl) },
            _ => result,
        };
    }

    /// <inheritdoc/>
    public string GetSanitizedContent(MasjidPost announcement)
    {
        return HtmlSanitizer.Sanitize(announcement.Description);
    }

    /// <summary>
    /// Resolves each announcement's relative <see cref="Models.MasjidPost.ImageUrl"/> against
    /// <paramref name="baseUrl"/> (API-10) and decodes HTML entities in the title.
    /// Absolute or blank URLs are left unchanged.
    /// </summary>
    private static List<MasjidPost> Normalize(List<MasjidPost> items, string baseUrl)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var resolved = EndpointUrlResolver.Resolve(items[i].ImageUrl, baseUrl);
            if (!string.Equals(resolved, items[i].ImageUrl, StringComparison.Ordinal))
            {
                items[i] = items[i] with { ImageUrl = resolved };
            }

            // NFR-RL-01: WordPress encodes punctuation in titles (e.g. &#8211;).
            var title = HtmlSanitizer.ToPlainText(items[i].Title);
            if (!string.Equals(title, items[i].Title, StringComparison.Ordinal))
            {
                items[i] = items[i] with { Title = title };
            }
        }

        return items;
    }
}
