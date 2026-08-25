using System.Net;
using FluentAssertions;
using MasjidApp.Core.Caching;
using MasjidApp.Core.Events;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;
using MasjidApp.Core.Services;

namespace MasjidApp.Core.Tests.Events;

// QA-02, QA-06: integration-style tests that exercise EventsService with a real
// MasjidApiClient wired to a stub HTTP handler and in-memory cache.

/// <summary>
/// Integration tests for <see cref="EventsService"/> wired to a stub
/// <see cref="MasjidApiClient"/>.  Verifies fetch-and-return, sort order, and
/// RRULE expansion without hitting a live network.
/// </summary>
public sealed class EventsServiceIntegrationTests
{
    // ── Test fixtures ────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 6, 12, 18, 0, 0, TimeSpan.FromHours(-7));

    private const string FakeEventsUrl = "https://masjid.test/events";

    /// <summary>One-time fundraiser event on 2026-06-20.</summary>
    private static readonly MasjidPost FundraiserEvent = new()
    {
        Title = "Community Fundraising Dinner",
        Description = "<p>Annual fundraiser dinner.</p>",
        EventDateTime = new DateTimeOffset(2026, 6, 20, 18, 0, 0, TimeSpan.FromHours(-7)),
        Location = "Main Hall",
        Category = "fundraisers",
    };

    /// <summary>Weekly Friday halaqa starting 2026-06-12.</summary>
    private static readonly MasjidPost WeeklyHalaqa = new()
    {
        Title = "Weekly Halaqa",
        Description = "<p>Every Friday study circle.</p>",
        EventDateTime = new DateTimeOffset(2026, 6, 12, 19, 0, 0, TimeSpan.FromHours(-7)),
        Category = "classes",
        RecurrenceRule = "FREQ=WEEKLY;BYDAY=FR",
    };

    /// <summary>Past event that should be excluded from the upcoming window.</summary>
    private static readonly MasjidPost PastEvent = new()
    {
        Title = "Old Picnic",
        EventDateTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
    };

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="MasjidApiClient"/> backed by a stub handler that returns
    /// the provided JSON body for any request to <see cref="FakeEventsUrl"/>.
    /// </summary>
    private static (MasjidApiClient client, EventsService sut) BuildSut(string jsonBody)
    {
        var handler = new StubHttpHandler(FakeEventsUrl, jsonBody);
        var http = new HttpClient(handler);
        var cache = new MemoryCacheStore();
        var client = new MasjidApiClient(http, cache, pipeline: null);
        var sut = new EventsService(client);
        return (client, sut);
    }

    private static string SerializeEvents(params MasjidPost[] events)
    {
        // Minimal hand-rolled JSON array - avoids pulling in System.Text.Json options
        // that differ from the app's source-generated context.
        var items = events.Select(e =>
        {
            var dt = e.EventDateTime!.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
            var rrule = e.RecurrenceRule is not null ? $",\"recurrenceRule\":\"{e.RecurrenceRule}\"" : string.Empty;
            var loc = e.Location is not null ? $",\"location\":\"{e.Location}\"" : string.Empty;
            var cat = e.Category is not null ? $",\"category\":\"{e.Category}\"" : string.Empty;
            var desc = e.Description is not null ? $",\"description\":\"{e.Description.Replace("\"", "\\\"")}\"" : string.Empty;
            return $"{{\"title\":\"{e.Title}\",\"eventDateTime\":\"{dt}\"{desc}{loc}{cat}{rrule}}}";
        });
        return $"[{string.Join(",", items)}]";
    }

    // ── Tests ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Events fetched from a URL are returned, de-serialized, and sorted ascending by
    /// occurrence date/time (FR-EV-01).
    /// </summary>
    [Fact]
    public async Task GetEventsAsync_ThenGetUpcoming_ReturnsSortedOccurrences()
    {
        var json = SerializeEvents(FundraiserEvent, WeeklyHalaqa, PastEvent);
        var (_, sut) = BuildSut(json);

        var result = await sut.GetEventsAsync(FakeEventsUrl);

        result.Should().BeOfType<ApiResult<List<MasjidPost>>.Fresh>();
        var events = result.ValueOrDefault!;
        events.Should().HaveCount(3);
        events.Should().AllSatisfy(post => post.IsEvent.Should().BeTrue());

        var occurrences = sut.GetUpcoming(events, BaseTime, BaseTime.AddDays(30));

        occurrences.Should().NotBeEmpty();
        occurrences.Should().BeInAscendingOrder(o => o.OccurrenceDateTime);
        occurrences.Should().NotContain(o => o.Event.Title == "Old Picnic",
            "past events must be excluded from the upcoming window");
    }

    /// <summary>
    /// A config-provided relative events endpoint is resolved against the active masjid's
    /// config URL before the HTTP client sees it (API-10, FR-EV-01).
    /// </summary>
    [Fact]
    public async Task GetEventsAsync_RelativeEndpointUrl_ResolvedAgainstActiveConfigUrl()
    {
        const string configUrl = "https://masjid.test/wp-json/masjid/v1/config";
        const string relativeEventsUrl = "/wp-json/masjid/v1/events";
        const string resolvedEventsUrl = "https://masjid.test/wp-json/masjid/v1/events";

        var json = SerializeEvents(FundraiserEvent);
        var handler = new StubHttpHandler(resolvedEventsUrl, json);
        var http = new HttpClient(handler);
        var cache = new MemoryCacheStore();
        var client = new MasjidApiClient(http, cache, pipeline: null);
        var sut = new EventsService(client, new FakeMasjidContext(configUrl));

        var result = await sut.GetEventsAsync(relativeEventsUrl);

        result.Should().BeOfType<ApiResult<List<MasjidPost>>.Fresh>();
        result.ValueOrDefault.Should().ContainSingle(e => e.Title == FundraiserEvent.Title);
    }

    [Fact]
    public async Task GetEventsAsync_RelativeEndpointWithoutActiveConfig_ReturnsFailedResult()
    {
        var (_, sut) = BuildSut(SerializeEvents(FundraiserEvent));

        var result = await sut.GetEventsAsync("/wp-json/masjid/v1/events");

        result.Should().BeOfType<ApiResult<List<MasjidPost>>.Failed>();
    }

    /// <summary>
    /// A weekly RRULE event is expanded into multiple occurrences over a 30-day window
    /// (FR-EV-07, RRULE expansion).
    /// </summary>
    [Fact]
    public async Task GetUpcoming_WeeklyRRule_ExpandsCorrectNumberOfOccurrences()
    {
        var json = SerializeEvents(WeeklyHalaqa);
        var (_, sut) = BuildSut(json);

        var result = await sut.GetEventsAsync(FakeEventsUrl);
        var events = result.ValueOrDefault!;

        // 30-day window starting at BaseTime (Fri Jun 12 2026).
        // Fridays in window: Jun 12, Jun 19, Jun 26, Jul 3, Jul 10 → at least 4
        var from = BaseTime;
        var until = from.AddDays(30);

        var occurrences = sut.GetUpcoming(events, from, until);

        occurrences.Should().HaveCountGreaterThanOrEqualTo(4,
            "a weekly event over 30 days should produce at least 4 occurrences");
        occurrences.Should().AllSatisfy(o => o.Event.Title.Should().Be("Weekly Halaqa"));
        occurrences.Should().BeInAscendingOrder(o => o.OccurrenceDateTime);
    }

    /// <summary>
    /// The cache returns a <see cref="ApiResult{T}.NotModified"/> result on the second
    /// request, and the service still returns the same sorted occurrences (API-08, NFR-OF-03).
    /// </summary>
    [Fact]
    public async Task GetEventsAsync_SecondRequest_ReturnsFromCacheWhenNotModified()
    {
        var json = SerializeEvents(FundraiserEvent);
        var handler = new StubHttpHandler(FakeEventsUrl, json);
        var http = new HttpClient(handler);
        var cache = new MemoryCacheStore();
        var client = new MasjidApiClient(http, cache, pipeline: null);
        var sut = new EventsService(client);

        // First fetch populates the cache.
        var first = await sut.GetEventsAsync(FakeEventsUrl);
        first.Should().BeOfType<ApiResult<List<MasjidPost>>.Fresh>();

        // Second fetch with a 304 handler returns cached value.
        handler.ReturnNotModified = true;
        var second = await sut.GetEventsAsync(FakeEventsUrl);

        second.HasValue.Should().BeTrue("cached data must be returned on 304");
        second.ValueOrDefault!.Should().HaveCount(1);
    }

    /// <summary>
    /// WordPress encodes punctuation in titles (e.g. <c>&amp;#8211;</c>); the service
    /// decodes it so the UI never renders raw entities (NFR-RL-01).
    /// </summary>
    [Fact]
    public async Task GetEventsAsync_TitleWithHtmlEntities_IsDecoded()
    {
        const string json = """
            [{"title":"Essentials of Islam &#8211; Weekly Halaqa","eventDateTime":"2026-06-20T18:00:00-07:00"}]
            """;
        var (_, sut) = BuildSut(json);

        var result = await sut.GetEventsAsync(FakeEventsUrl);

        result.ValueOrDefault!.Single().Title.Should().Be("Essentials of Islam – Weekly Halaqa");
    }

    [Fact]
    public async Task GetEventsAsync_AlertHtml_IsConvertedToPlainText()
    {
        const string json = """
            [{
              "title": "Weekly Halaqa",
              "eventDateTime": "2026-06-20T18:00:00-07:00",
              "alert": "<strong>Canceled</strong> this week.<script>bad()</script>",
              "alertEndDateTime": "2026-06-21T23:59:59-07:00"
            }]
            """;
        var (_, sut) = BuildSut(json);

        var result = await sut.GetEventsAsync(FakeEventsUrl);

        var ev = result.ValueOrDefault!.Single();
        ev.Alert.Should().Be("Canceled this week.");
        ev.AlertEndDateTime.Should().Be(
            new DateTimeOffset(2026, 6, 21, 23, 59, 59, TimeSpan.FromHours(-7)));
    }

    /// <summary>
    /// <see cref="EventsService.FilterByCategory"/> correctly narrows occurrences by
    /// category (FR-EV-04).
    /// </summary>
    [Fact]
    public async Task FilterByCategory_ReturnsOnlyMatchingCategory()
    {
        var json = SerializeEvents(FundraiserEvent, WeeklyHalaqa);
        var (_, sut) = BuildSut(json);

        var result = await sut.GetEventsAsync(FakeEventsUrl);
        var occurrences = sut.GetUpcoming(result.ValueOrDefault!, BaseTime, BaseTime.AddDays(30));

        var fundraisers = sut.FilterByCategory(occurrences, "fundraisers");

        fundraisers.Should().NotBeEmpty();
        fundraisers.Should().AllSatisfy(o => o.Event.Category.Should().Be("fundraisers"));
        fundraisers.Should().NotContain(o => o.Event.Category == "classes");
    }

    /// <summary>
    /// A relative event <c>image</c> path is resolved to an absolute URL against the
    /// masjid base URL (the events endpoint origin); absolute and missing images are
    /// left unchanged (API-10).
    /// </summary>
    [Fact]
    public async Task GetEventsAsync_RelativeImagePath_ResolvedAgainstMasjidBaseUrl()
    {
        const string json = """
            [
              { "title": "Relative", "eventDateTime": "2026-06-20T18:00:00-07:00", "image": "/images/dinner.png" },
              { "title": "Absolute", "eventDateTime": "2026-06-21T18:00:00-07:00", "image": "https://cdn.example.com/a.png" },
              { "title": "None", "eventDateTime": "2026-06-22T18:00:00-07:00" }
            ]
            """;
        var (_, sut) = BuildSut(json);

        var result = await sut.GetEventsAsync(FakeEventsUrl);

        var events = result.ValueOrDefault!;
        events.Single(e => e.Title == "Relative").ImageUrl
            .Should().Be("https://masjid.test/images/dinner.png");
        events.Single(e => e.Title == "Absolute").ImageUrl
            .Should().Be("https://cdn.example.com/a.png");
        events.Single(e => e.Title == "None").ImageUrl
            .Should().BeNull();
    }

    // ── Infrastructure stubs ─────────────────────────────────────────────────────

    private sealed class StubHttpHandler(string expectedUrl, string responseBody)
        : HttpMessageHandler
    {
        public bool ReturnNotModified { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.ToString() != expectedUrl)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (ReturnNotModified)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(response);
        }
    }

    private sealed class FakeMasjidContext(string configUrl) : IMasjidContext
    {
        public MasjidStoredEntry? ActiveEntry { get; } = new("icob", "Islamic Center of Bothell", configUrl);

        public event EventHandler ActiveMasjidChanged = static (_, _) => { };

        public Task<ApiResult<MasjidConfig>?> GetConfigAsync(
            ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
            CancellationToken ct = default) =>
            Task.FromResult<ApiResult<MasjidConfig>?>(null);

        public void SetActive(MasjidStoredEntry entry)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>In-memory <see cref="ICacheStore"/> for test isolation.</summary>
    private sealed class MemoryCacheStore : ICacheStore
    {
        private readonly Dictionary<string, CacheEntry> _store = [];

        public Task<CacheEntry?> GetAsync(string key, CancellationToken ct = default)
        {
            _store.TryGetValue(key, out var entry);
            return Task.FromResult(entry);
        }

        public Task SetAsync(CacheEntry entry, CancellationToken ct = default)
        {
            _store[entry.Key] = entry;
            return Task.CompletedTask;
        }
    }
}
