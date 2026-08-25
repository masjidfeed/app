using FluentAssertions;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Tests.Configuration;

public sealed class TenantBrandingTests
{
    [Theory]
    [InlineData("icob", "Any Name", "https://example.org/wp-json/masjid/v1/config")]
    [InlineData("other", "Islamic Center of Bothell", "https://example.org/config")]
    [InlineData("other", "Other", "https://bothellmosque.org/wp-json/masjid/v1/config")]
    public void Get_EntryDoesNotMatchIdAndHost_ReturnsNull(string id, string name, string url)
    {
        TenantBranding.Get(new MasjidStoredEntry(id, name, url)).Should().BeNull();
    }

    [Theory]
    [InlineData("icob", "https://bothellmosque.org/wp-json/masjid/v1/config", "icob")]
    [InlineData("icob", "https://api.bothellmosque.org/config", "icob")]
    [InlineData("maps", "https://mapsredmond.org/config", "maps")]
    [InlineData("maps", "https://api.mapsredmond.org/config", "maps")]
    public void Get_MatchingRegisteredTenant_ReturnsBranding(
        string id,
        string url,
        string expectedIconName)
    {
        var branding = TenantBranding.Get(new MasjidStoredEntry(id, "Any Name", url));

        branding.Should().NotBeNull();
        branding!.IosAlternateIconName.Should().Be(expectedIconName.ToUpperInvariant());
    }
}
