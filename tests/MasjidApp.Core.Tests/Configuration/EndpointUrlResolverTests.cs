using FluentAssertions;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Tests.Configuration;

// API-10: relative per-masjid endpoint URLs must resolve against the masjid's own
// config-document origin, not the environment base URL.

public sealed class EndpointUrlResolverTests
{
    private const string ConfigUrl = "https://masjid-a.example.com/wp-json/masjid/v1/config";

    // ── Single-value resolution ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NullOrBlank_ReturnedUnchanged(string? input)
    {
        EndpointUrlResolver.Resolve(input, ConfigUrl).Should().Be(input);
    }

    [Theory]
    [InlineData("https://other.example.com/salahapi")]
    [InlineData("http://localhost:8090/salahapi")]
    public void Resolve_AbsoluteHttpUrl_ReturnedUnchanged(string input)
    {
        EndpointUrlResolver.Resolve(input, ConfigUrl).Should().Be(input);
    }

    [Fact]
    public void Resolve_RootRelative_ResolvesAgainstConfigOrigin()
    {
        EndpointUrlResolver.Resolve("/salahapi", ConfigUrl)
            .Should().Be("https://masjid-a.example.com/salahapi");
    }

    [Fact]
    public void Resolve_PathRelative_ResolvesAgainstConfigDirectory()
    {
        // RFC 3986: replaces the last segment ("config") of the base path.
        EndpointUrlResolver.Resolve("salahapi", ConfigUrl)
            .Should().Be("https://masjid-a.example.com/wp-json/masjid/v1/salahapi");
    }

    [Fact]
    public void Resolve_ProtocolRelative_AdoptsConfigScheme()
    {
        EndpointUrlResolver.Resolve("//cdn.example.com/salahapi", ConfigUrl)
            .Should().Be("https://cdn.example.com/salahapi");
    }

    [Fact]
    public void Resolve_RelativeUrl_PreservesQueryAndPort()
    {
        const string baseUrl = "http://10.0.2.2:8090/config";

        EndpointUrlResolver.Resolve("/events?lang=en", baseUrl)
            .Should().Be("http://10.0.2.2:8090/events?lang=en");
    }

    [Fact]
    public void Resolve_RelativeUrl_WithInvalidBase_ReturnedUnchanged()
    {
        EndpointUrlResolver.Resolve("/salahapi", "not a url")
            .Should().Be("/salahapi");
    }

    [Fact]
    public void Resolve_RelativeUrl_WithNullBase_ReturnedUnchanged()
    {
        EndpointUrlResolver.Resolve("/salahapi", null)
            .Should().Be("/salahapi");
    }

    // ── Origin-relative content URL resolution ─────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveAgainstOrigin_NullOrBlank_ReturnsNull(string? input)
    {
        EndpointUrlResolver.ResolveAgainstOrigin(input, ConfigUrl).Should().BeNull();
    }

    [Theory]
    [InlineData("https://cdn.example.com/logo.png")]
    [InlineData("http://localhost:8090/logo.png")]
    public void ResolveAgainstOrigin_AbsoluteHttpUrl_ReturnedUnchanged(string input)
    {
        EndpointUrlResolver.ResolveAgainstOrigin(input, ConfigUrl).Should().Be(input);
    }

    [Fact]
    public void ResolveAgainstOrigin_RootRelative_ResolvesAgainstConfigOrigin()
    {
        EndpointUrlResolver.ResolveAgainstOrigin("/wp-content/logo.png", ConfigUrl)
            .Should().Be("https://masjid-a.example.com/wp-content/logo.png");
    }

    [Fact]
    public void ResolveAgainstOrigin_PathRelative_ResolvesAgainstConfigOrigin()
    {
        EndpointUrlResolver.ResolveAgainstOrigin("logo.png", ConfigUrl)
            .Should().Be("https://masjid-a.example.com/logo.png");
    }

    [Fact]
    public void ResolveAgainstOrigin_ProtocolRelative_AdoptsConfigScheme()
    {
        EndpointUrlResolver.ResolveAgainstOrigin("//cdn.example.com/logo.png", ConfigUrl)
            .Should().Be("https://cdn.example.com/logo.png");
    }

    [Fact]
    public void ResolveAgainstOrigin_RelativeUrl_WithInvalidBase_ReturnsNull()
    {
        EndpointUrlResolver.ResolveAgainstOrigin("/logo.png", "not a url")
            .Should().BeNull();
    }

    // ── EndpointsConfig resolution ──────────────────────────────────────────────

    [Fact]
    public void Resolve_EndpointsConfig_Null_ReturnsNull()
    {
        EndpointUrlResolver.Resolve((EndpointsConfig?)null, ConfigUrl).Should().BeNull();
    }

    [Fact]
    public void Resolve_EndpointsConfig_ResolvesEachFieldIndependently()
    {
        var endpoints = new EndpointsConfig
        {
            SalahApiUrl = "/salahapi",
            EventsUrl = "events",
            AnnouncementsUrl = "https://other.example.com/announcements",
        };

        var resolved = EndpointUrlResolver.Resolve(endpoints, ConfigUrl);

        resolved!.SalahApiUrl.Should().Be("https://masjid-a.example.com/salahapi");
        resolved.EventsUrl.Should().Be("https://masjid-a.example.com/wp-json/masjid/v1/events");
        resolved.AnnouncementsUrl.Should().Be("https://other.example.com/announcements");
    }

    // ── MasjidConfig resolution ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_MasjidConfig_ResolvesEndpointsAndPreservesOtherFields()
    {
        var config = new MasjidConfig
        {
            Masjid = new MasjidIdentity { Id = "icob", Name = "Test", Timezone = "America/Los_Angeles" },
            Endpoints = new EndpointsConfig { SalahApiUrl = "/salahapi" },
            PushNotifications = new PushNotificationsConfig { RegistrationUrl = "/push/registrations" },
        };

        var resolved = EndpointUrlResolver.Resolve(config, ConfigUrl);

        resolved.Endpoints!.SalahApiUrl.Should().Be("https://masjid-a.example.com/salahapi");
        resolved.PushNotifications!.RegistrationUrl.Should().Be("https://masjid-a.example.com/push/registrations");
        resolved.Masjid.Should().BeSameAs(config.Masjid);
    }

    [Fact]
    public void Resolve_MasjidConfig_NullEndpoints_DoesNotThrow()
    {
        var config = new MasjidConfig
        {
            Masjid = new MasjidIdentity { Id = "icob", Name = "Test", Timezone = "America/Los_Angeles" },
        };

        var resolved = EndpointUrlResolver.Resolve(config, ConfigUrl);

        resolved.Endpoints.Should().BeNull();
    }
}
