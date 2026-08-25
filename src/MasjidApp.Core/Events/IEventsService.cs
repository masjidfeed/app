using MasjidApp.Core.Http;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Events;

// API-05, FR-EV-06, FR-EV-07

/// <summary>
/// Domain service for fetching, caching, and querying masjid events.
/// </summary>
public interface IEventsService
{
    /// <summary>
    /// Fetches the events list from <paramref name="eventsUrl"/> using the local cache
    /// for conditional requests and offline fallback (FR-EV-06).
    /// </summary>
    /// <param name="eventsUrl">Absolute endpoint URL, or a relative URL resolvable against the active masjid config URL.</param>
    /// <param name="cachePolicy">Whether a fresh cached payload may bypass the network.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ApiResult<List<MasjidPost>>> GetEventsAsync(
        string eventsUrl,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all concrete occurrences of the supplied events that fall within
    /// [<paramref name="from"/>, <paramref name="until"/>], sorted ascending by occurrence
    /// date/time.  Recurring events are expanded via RRULE; non-recurring events
    /// produce a single occurrence if they fall within the window (FR-EV-07).
    /// </summary>
    IReadOnlyList<MasjidEventOccurrence> GetUpcoming(
        IEnumerable<MasjidPost> events,
        DateTimeOffset from,
        DateTimeOffset until);

    /// <summary>Filters occurrences by <paramref name="category"/> (case-insensitive).</summary>
    IReadOnlyList<MasjidEventOccurrence> FilterByCategory(
        IEnumerable<MasjidEventOccurrence> occurrences,
        string category);

    /// <summary>
    /// Returns occurrences whose event title or description contains
    /// <paramref name="query"/> (case-insensitive).
    /// </summary>
    IReadOnlyList<MasjidEventOccurrence> Search(
        IEnumerable<MasjidEventOccurrence> occurrences,
        string query);
}
