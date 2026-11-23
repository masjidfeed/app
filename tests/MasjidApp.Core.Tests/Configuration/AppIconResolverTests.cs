using FluentAssertions;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Tests.Configuration;

public sealed class AppIconResolverTests
{
    private static TenantBrandingDefinition Tenant(string id, string url) =>
        TenantBranding.Get(new MasjidStoredEntry(id, "Any Name", url))!;

    [Fact]
    public void Resolve_Light_ReturnsLightAssets()
    {
        var icon = AppIconResolver.Resolve(
            AppIconPreference.Light,
            Tenant("icob", "https://bothellmosque.org/config"));

        icon.IosAlternateIconName.Should().Be("MASJIDFEED_LIGHT");
        icon.AndroidLauncherAlias.Should().Be(".MasjidFeedLightLauncher");
    }

    [Fact]
    public void Resolve_Dark_ReturnsDefaultAssets()
    {
        var icon = AppIconResolver.Resolve(
            AppIconPreference.Dark,
            Tenant("icob", "https://bothellmosque.org/config"));

        icon.IosAlternateIconName.Should().BeNull();
        icon.AndroidLauncherAlias.Should().Be(".GenericLauncher");
    }

    [Fact]
    public void Resolve_MasjidWithoutBranding_ReturnsDefaultAssets()
    {
        var icon = AppIconResolver.Resolve(AppIconPreference.Masjid, null);

        icon.IosAlternateIconName.Should().BeNull();
        icon.AndroidLauncherAlias.Should().Be(".GenericLauncher");
    }

    [Fact]
    public void Resolve_MasjidWithBranding_ReturnsTenantAssets()
    {
        var branding = TenantBranding.Get(
            new MasjidStoredEntry("masjid-312fc341", "Islamic Center of Bothell", "https://bothellmosque.org/wp-json/masjid/v1/config"));

        var icon = AppIconResolver.Resolve(AppIconPreference.Masjid, branding);

        icon.IosAlternateIconName.Should().Be("ICOB");
        icon.AndroidLauncherAlias.Should().Be(".IcobLauncher");
    }
}
