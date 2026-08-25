using System.Diagnostics.CodeAnalysis;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;
using MasjidApp.Core.Services;
using MasjidApp.Core.Utils;

namespace MasjidApp.Core.Events;

// API-05, API-10, FR-EV-06, FR-EV-07, NFR-RL-01

/// <summary>
/// Fetches, caches, and queries masjid events, including RRULE expansion for
/// recurring events (FR-EV-07).
/// </summary>
public sealed class EventsService : IEventsService
{
    private readonly IMasjidApiClient _client;
    private readonly IMasjidContext? _context;

    /// <summary>Initializes a new instance with the given API client.</summary>
    public EventsService(IMasjidApiClient client, IMasjidContext? context = null)
    {
        _client = client;
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<ApiResult<List<MasjidPost>>> GetEventsAsync(
        string eventsUrl,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        var resolvedEventsUrl = EndpointUrlResolver.Resolve(eventsUrl, _context?.ActiveEntry?.ConfigUrl);
        if (!IsAbsoluteHttpUrl(resolvedEventsUrl))
        {
            return new ApiResult<List<MasjidPost>>.Failed(
                new ArgumentException($"Events endpoint URL must be absolute or resolvable against the active masjid config URL. Got: {eventsUrl}", nameof(eventsUrl)),
                "Events endpoint URL is not valid.");
        }

        var result = await _client
            .GetAsync(resolvedEventsUrl, EventsJsonContext.Default.ListMasjidPost, cachePolicy, ct)
            .ConfigureAwait(false);

        // API-10: each event's image may be supplied relative to the masjid's base URL;
        // resolve it against the (absolute) endpoint URL so it loads correctly.
        return result switch
        {
            ApiResult<List<MasjidPost>>.Fresh f =>
                f with { Value = Normalize(f.Value, resolvedEventsUrl) },
            ApiResult<List<MasjidPost>>.Cached c =>
                c with { Value = Normalize(c.Value, resolvedEventsUrl) },
            ApiResult<List<MasjidPost>>.NotModified nm =>
                nm with { Value = Normalize(nm.Value, resolvedEventsUrl) },
            ApiResult<List<MasjidPost>>.Stale s =>
                s with { Value = Normalize(s.Value, resolvedEventsUrl) },
            _ => result,
        };
    }

    private static bool IsAbsoluteHttpUrl([NotNullWhen(true)] string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Resolves each event's relative <see cref="Models.MasjidPost.ImageUrl"/> against
    /// <paramref name="baseUrl"/> (API-10), decodes HTML entities in the title, and
    /// converts event alerts to safe plain text.
    /// Absolute or blank URLs are left unchanged.
    /// </summary>
    private static List<MasjidPost> Normalize(List<MasjidPost> items, string baseUrl)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (!items[i].IsEvent)
            {
                items[i] = items[i] with { IsEvent = true };
            }

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

            var alert = HtmlSanitizer.ToPlainText(items[i].Alert);
            alert = string.IsNullOrWhiteSpace(alert) ? null : alert;
            if (!string.Equals(alert, items[i].Alert, StringComparison.Ordinal))
            {
                items[i] = items[i] with { Alert = alert };
            }
        }

        return items;
    }

    /// <inheritdoc/>
    public IReadOnlyList<MasjidEventOccurrence> GetUpcoming(
        IEnumerable<MasjidPost> events,
        DateTimeOffset from,
        DateTimeOffset until)
    {
        var result = new List<MasjidEventOccurrence>();

        foreach (var ev in events)
        {
            if (ev.EventDateTime is not { } eventDateTime)
            {
                continue;
            }

            var activeAlert = GetActiveAlert(ev, from);
            if (!string.IsNullOrWhiteSpace(ev.RecurrenceRule))
            {
                // Expand RRULE over the requested window.
                var hits = RRuleExpander.Expand(ev.RecurrenceRule, eventDateTime, from, until);
                foreach (var odt in hits)
                {
                    result.Add(new MasjidEventOccurrence(ev, odt, activeAlert));
                }
            }
            else if (eventDateTime >= from && eventDateTime <= until)
            {
                // One-time event within window.
                result.Add(new MasjidEventOccurrence(ev, eventDateTime, activeAlert));
            }
        }

        return result.OrderBy(o => o.OccurrenceDateTime).ToList();
    }

    private static string? GetActiveAlert(MasjidPost ev, DateTimeOffset at)
    {
        return !string.IsNullOrWhiteSpace(ev.Alert) &&
            ev.AlertEndDateTime is { } end &&
            at <= end
                ? ev.Alert
                : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<MasjidEventOccurrence> FilterByCategory(
        IEnumerable<MasjidEventOccurrence> occurrences,
        string category)
    {
        return occurrences
            .Where(o => string.Equals(o.Event.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<MasjidEventOccurrence> Search(
        IEnumerable<MasjidEventOccurrence> occurrences,
        string query)
    {
        return occurrences
            .Where(o =>
                (o.Event.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.Event.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }
}
