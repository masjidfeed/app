using FluentAssertions;
using MasjidApp.Core.Push;

namespace MasjidApp.Core.Tests.Push;

public sealed class PostDeepLinkTests
{
    [Theory]
    [InlineData("announcements", PostDeepLinkType.Announcements)]
    [InlineData("events", PostDeepLinkType.Events)]
    public void TryParse_DocumentedPostLink_ReturnsTarget(string pathType, PostDeepLinkType expectedType)
    {
        var uri = new Uri($"masjidapp://open/v1/masjids/islamic-center-of-bothell/{pathType}/456");

        var parsed = PostDeepLink.TryParse(uri, out var result);

        parsed.Should().BeTrue();
        result.Should().Be(new PostDeepLink("islamic-center-of-bothell", expectedType, 456));
    }

    [Theory]
    [InlineData("https://example.org/v1/masjids/test/events/1")]
    [InlineData("masjidapp://open/v2/masjids/test/events/1")]
    [InlineData("masjidapp://open/v1/masjids/test/prayers/1")]
    [InlineData("masjidapp://open/v1/masjids/test/events/0")]
    [InlineData("masjidapp://open/v1/masjids/test/events/not-a-number")]
    public void TryParse_UnsupportedOrMalformedLink_ReturnsFalse(string value)
    {
        var parsed = PostDeepLink.TryParse(new Uri(value), out var result);

        parsed.Should().BeFalse();
        result.Should().BeNull();
    }
}