using FluentAssertions;
using MasjidApp.Core.Configuration;

namespace MasjidApp.Core.Tests.Configuration;

public sealed class MasjidConfigUrlTests
{
    [Theory]
    [InlineData("https://bothellmosque.org", "https://bothellmosque.org/wp-json/masjid/v1/config")]
    [InlineData("https://example.org/wp-json/masjid/v1/config", "https://example.org/wp-json/masjid/v1/config")]
    [InlineData("bothellmosque.org", "https://bothellmosque.org/wp-json/masjid/v1/config")]
    [InlineData("  bothellmosque.org  ", "https://bothellmosque.org/wp-json/masjid/v1/config")]
    [InlineData("http://localhost:8090", "http://localhost:8090/wp-json/masjid/v1/config")]
    public void Resolve_WebsiteOrConfigUrl_ReturnsConfigEndpoint(string input, string expected)
    {
        MasjidConfigUrl.Resolve(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://bothellmosque.org", "https://bothellmosque.org/")]
    [InlineData("https://example.org/wp-json/masjid/v1/config", "https://example.org/")]
    [InlineData("http://localhost:8090/path", "http://localhost:8090/path")]
    [InlineData("bothellmosque.org", "https://bothellmosque.org/")]
    public void ResolveWebsite_ValidUrl_ReturnsWebsiteOrigin(string input, string expected)
    {
        MasjidConfigUrl.ResolveWebsite(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://bothellmosque.org", true)]
    [InlineData("http://localhost:8090", true)]
    [InlineData("http://10.0.2.2:8090", true)]
    [InlineData("http://example.org", false)]
    [InlineData("javascript:alert(1)", false)]
    public void IsAllowed_SchemeAndHost_ReturnsExpected(string input, bool expected)
    {
        MasjidConfigUrl.IsAllowed(input).Should().Be(expected);
    }
}
