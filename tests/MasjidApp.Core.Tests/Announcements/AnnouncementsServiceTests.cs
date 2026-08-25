using System.Net;
using System.Text.Json;
using FluentAssertions;
using MasjidApp.Core.Announcements;
using MasjidApp.Core.Caching;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Tests.Announcements;

// QA-02, NFR-RL-01: parsing tests for the Announcement model.

/// <summary>
/// Unit tests for <see cref="MasjidPost"/> JSON deserialization and
/// <see cref="AnnouncementsService"/> pure logic.
/// </summary>
public sealed class AnnouncementsServiceTests
{
    // Pass null! for the client - only pure (non-network) methods are tested here.
    private readonly AnnouncementsService _sut = new(null!);

    private const string MockJson = """
        [
          {
            "title": "Eid al-Adha Prayer Times Announced",
            "description": "<p>Eid prayer will be held at <strong>7:30 AM</strong>.</p>",
            "publishedAt": "2026-06-05T09:00:00-07:00",
            "image": "http://localhost:8090/images/eid.png"
          },
          {
            "title": "Parking Lot Maintenance This Weekend",
            "description": "<p>The north parking lot will be closed for resurfacing on Saturday.</p>",
            "publishedAt": "2026-06-08T12:00:00-07:00",
            "image": null
          }
        ]
        """;

    // ── JSON parsing ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseJson_TwoItems_ReturnsBothAnnouncements()
    {
        var result = JsonSerializer.Deserialize(
            MockJson,
            AnnouncementsJsonContext.Default.ListMasjidPost);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void ParseJson_FirstItem_TitleAndPublishedAtAreCorrect()
    {
        var result = JsonSerializer.Deserialize(
            MockJson,
            AnnouncementsJsonContext.Default.ListMasjidPost)!;

        var first = result[0];
        first.Title.Should().Be("Eid al-Adha Prayer Times Announced");
        first.PublishedAt.Should().Be(
            new DateTimeOffset(2026, 6, 5, 9, 0, 0, TimeSpan.FromHours(-7)));
        first.ImageUrl.Should().Be("http://localhost:8090/images/eid.png");
    }

    [Fact]
    public void ParseJson_SecondItem_NullImageHandledGracefully()
    {
        var result = JsonSerializer.Deserialize(
            MockJson,
            AnnouncementsJsonContext.Default.ListMasjidPost)!;

        var second = result[1];
        second.ImageUrl.Should().BeNull();
        second.Title.Should().Be("Parking Lot Maintenance This Weekend");
    }

    [Fact]
    public async Task GetAnnouncementsAsync_ProductionDescription_PopulatesContent()
    {
        const string announcementsUrl = "https://masjid.test/announcements";
        const string json = """
            [{
              "title": "Get Out The Vote!",
              "description": "<p>Mail your ballot today!</p>",
              "publishedAt": "2026-08-02T03:05:53+00:00"
            }]
            """;
        var client = new MasjidApiClient(
            new HttpClient(new StubHttpHandler(announcementsUrl, json)),
            new MemoryCacheStore(),
            pipeline: null);
        var sut = new AnnouncementsService(client);

        var result = await sut.GetAnnouncementsAsync(announcementsUrl);

        var item = result.ValueOrDefault.Should().ContainSingle().Subject;
        item.Description.Should().Be("<p>Mail your ballot today!</p>");
        sut.GetSanitizedContent(item).Should().Contain("Mail your ballot today!");
    }

    [Fact]
    public void ParseJson_MissingOptionalFields_DefaultsApplied()
    {
        const string minimalJson = """[{"title":"Test","publishedAt":"2026-01-01T00:00:00Z"}]""";

        var result = JsonSerializer.Deserialize(
            minimalJson,
            AnnouncementsJsonContext.Default.ListMasjidPost)!;

        var item = result.Should().ContainSingle().Which;
        item.Title.Should().Be("Test");
        item.Description.Should().BeNull();
        item.ImageUrl.Should().BeNull();
    }

    [Fact]
    public void ParseJson_UnknownExtraFields_AreTolerated()
    {
        const string withExtras = """
            [{"title":"T","publishedAt":"2026-01-01T00:00:00Z","futureField":"x","count":42}]
            """;

        var act = () => JsonSerializer.Deserialize(
            withExtras,
            AnnouncementsJsonContext.Default.ListMasjidPost);

        act.Should().NotThrow();
    }

    // ── GetSanitizedContent ─────────────────────────────────────────────────────

    [Fact]
    public void GetSanitizedContent_WithScript_ReturnsStrippedContent()
    {
        var announcement = new MasjidPost
        {
            Title = "Test",
            Description = "<p>Safe</p><script>bad()</script>",
            PublishedAt = DateTimeOffset.UtcNow,
        };

        var result = _sut.GetSanitizedContent(announcement);

        result.Should().NotContain("<script");
        result.Should().Contain("<p>Safe</p>");
    }

    [Fact]
    public void GetSanitizedContent_NullContent_ReturnsEmptyString()
    {
        var announcement = new MasjidPost
        {
            Title = "Test",
            Description = null,
            PublishedAt = DateTimeOffset.UtcNow,
        };

        var result = _sut.GetSanitizedContent(announcement);

        result.Should().BeEmpty();
    }

    // ── Image URL resolution (API-10) ───────────────────────────────────────────

    /// <summary>
    /// A relative announcement <c>image</c> path is resolved to an absolute URL against
    /// the masjid base URL (the announcements endpoint origin); absolute and missing
    /// images are left unchanged (API-10).
    /// </summary>
    [Fact]
    public async Task GetAnnouncementsAsync_RelativeImagePath_ResolvedAgainstMasjidBaseUrl()
    {
        const string announcementsUrl = "https://masjid.test/announcements";
        const string json = """
            [
              { "title": "Relative", "publishedAt": "2026-06-05T09:00:00-07:00", "image": "/images/eid.png" },
              { "title": "Absolute", "publishedAt": "2026-06-06T09:00:00-07:00", "image": "https://cdn.example.com/a.png" },
              { "title": "None", "publishedAt": "2026-06-07T09:00:00-07:00" }
            ]
            """;
        var handler = new StubHttpHandler(announcementsUrl, json);
        var client = new MasjidApiClient(
            new HttpClient(handler),
            new MemoryCacheStore(),
            pipeline: null);
        var sut = new AnnouncementsService(client);

        var result = await sut.GetAnnouncementsAsync(announcementsUrl);

        var items = result.ValueOrDefault!;
        items.Single(a => a.Title == "Relative").ImageUrl
            .Should().Be("https://masjid.test/images/eid.png");
        items.Single(a => a.Title == "Absolute").ImageUrl
            .Should().Be("https://cdn.example.com/a.png");
        items.Single(a => a.Title == "None").ImageUrl
            .Should().BeNull();
    }

    /// <summary>
    /// WordPress encodes punctuation in titles (e.g. <c>&amp;#8211;</c>); the service
    /// decodes it so the UI never renders raw entities (NFR-RL-01).
    /// </summary>
    [Fact]
    public async Task GetAnnouncementsAsync_TitleWithHtmlEntities_IsDecoded()
    {
        const string announcementsUrl = "https://masjid.test/announcements";
        const string json = """
            [{ "title": "Essentials of Islam &#8211; Weekly Halaqa", "publishedAt": "2026-06-05T09:00:00-07:00" }]
            """;
        var handler = new StubHttpHandler(announcementsUrl, json);
        var client = new MasjidApiClient(
            new HttpClient(handler),
            new MemoryCacheStore(),
            pipeline: null);
        var sut = new AnnouncementsService(client);

        var result = await sut.GetAnnouncementsAsync(announcementsUrl);

        result.ValueOrDefault!.Single().Title.Should().Be("Essentials of Islam – Weekly Halaqa");
    }

    // ── Infrastructure stubs ─────────────────────────────────────────────────────

    private sealed class StubHttpHandler(string expectedUrl, string responseBody)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.ToString() != expectedUrl)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

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
