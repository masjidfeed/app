using System.Net;
using FluentAssertions;
using MasjidApp.Core.Caching;
using MasjidApp.Core.Http;
using MasjidApp.Core.Posts;

namespace MasjidApp.Core.Tests.Posts;

public sealed class PostDetailsServiceTests
{
    [Fact]
    public async Task GetPostAsync_Announcement_ReturnsHydratedPost()
    {
        const string expectedUrl = "https://masjid.test/wp-json/masjid/v1/posts/456";
        const string json = """
            {
              "postId": 456,
              "title": "Parking &#8211; update",
              "description": "<p>Use overflow parking.</p>",
              "postUrl": "https://masjid.test/wp-json/masjid/v1/posts/456",
              "publishedAt": "2026-08-03T17:15:00Z",
              "image": "/uploads/parking.jpg",
              "url": "https://masjid.test/parking-update/"
            }
            """;
        var sut = CreateService(expectedUrl, json);

        var result = await sut.GetPostAsync(
            "https://masjid.test/wp-json/masjid/v1/announcements",
            456);

        result.ValueOrDefault.Should().NotBeNull();
        var post = result.ValueOrDefault!;
        post.PostId.Should().Be(456);
        post.Title.Should().Be("Parking – update");
        post.ImageUrl.Should().Be("https://masjid.test/uploads/parking.jpg");
        post.IsEvent.Should().BeFalse();
    }

    [Fact]
    public async Task GetPostAsync_Event_ReturnsEventFields()
    {
        const string expectedUrl = "https://masjid.test/wp-json/masjid/v1/posts/123";
        const string json = """
            {
              "postId": 123,
              "title": "Community Dinner",
              "publishedAt": "2026-07-15T18:00:00Z",
              "isEvent": true,
              "location": "Main prayer hall",
              "eventDateTime": "2026-08-07T19:30:00-07:00",
              "url": "https://masjid.test/community-dinner/"
            }
            """;
        var sut = CreateService(expectedUrl, json);

        var post = (await sut.GetPostAsync(
            "https://masjid.test/wp-json/masjid/v1/events",
            123)).ValueOrDefault!;

        post.IsEvent.Should().BeTrue();
        post.Location.Should().Be("Main prayer hall");
        post.EventDateTime.Should().Be(
            new DateTimeOffset(2026, 8, 7, 19, 30, 0, TimeSpan.FromHours(-7)));
    }

    private static PostDetailsService CreateService(string expectedUrl, string json) =>
        new(new MasjidApiClient(
            new HttpClient(new StubHandler(expectedUrl, json)),
            new MemoryCacheStore(),
            pipeline: null));

    private sealed class StubHandler(string expectedUrl, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri?.ToString() == expectedUrl
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class MemoryCacheStore : ICacheStore
    {
        private readonly Dictionary<string, CacheEntry> _entries = [];

        public Task<CacheEntry?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_entries.GetValueOrDefault(key));

        public Task SetAsync(CacheEntry entry, CancellationToken ct = default)
        {
            _entries[entry.Key] = entry;
            return Task.CompletedTask;
        }
    }
}