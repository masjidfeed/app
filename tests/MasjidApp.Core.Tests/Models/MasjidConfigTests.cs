using System.Text.Json;
using FluentAssertions;
using MasjidApp.Core.Models;
using MasjidApp.Core.Serialization;

namespace MasjidApp.Core.Tests.Models;

// QA-02, QA-03: verifies that MasjidConfig deserializes the mock/config.json contract
// shape correctly, and that missing/extra fields are handled tolerantly (API-07).

public sealed class MasjidConfigTests
{
    // ── Mock contract shape ────────────────────────────────────────────────────

    private const string MockConfigJson = """
        {
          "masjid": {
            "id": "icob",
            "name": "Islamic Center of Bothell",
            "timezone": "America/Los_Angeles"
          },
          "branding": {
            "logoUrl": "http://localhost:8090/images/logo.png",
            "splashLogoUrl": "http://localhost:8090/images/splash.png",
            "primaryColor": "#1B7F5C",
            "primaryColorDark": "#3FBF8F"
          },
          "endpoints": {
            "salahApiUrl": "http://localhost:8090/salahapi",
            "eventsUrl": "http://localhost:8090/events",
            "announcementsUrl": "http://localhost:8090/announcements"
          },
          "contact": {
            "address": "10921 NE 191st St, Bothell, WA 98011, USA",
            "phone": "+1-425-000-0000",
            "email": "info@example.org",
            "website": "https://example.org"
          },
          "donationUrl": "https://example.org/donate",
          "social": {
            "facebook": "https://facebook.com/example",
            "instagram": "https://instagram.com/example",
            "whatsapp": "https://chat.whatsapp.com/example"
          },
          "featureFlags": {
            "events": true,
            "announcements": true,
            "donations": true,
            "qibla": true,
            "prayerReminders": true
          }
        }
        """;

    [Fact]
    public void Deserialize_MockConfigJson_ParsesAllFields()
    {
        var config = JsonSerializer.Deserialize(MockConfigJson, MasjidCoreJsonContext.Default.MasjidConfig);

        config.Should().NotBeNull();

        // Identity
        config!.Masjid.Should().NotBeNull();
        config.Masjid!.Id.Should().Be("icob");
        config.Masjid.Name.Should().Be("Islamic Center of Bothell");
        config.Masjid.Timezone.Should().Be("America/Los_Angeles");

        // Branding
        config.Branding.Should().NotBeNull();
        config.Branding!.PrimaryColor.Should().Be("#1B7F5C");
        config.Branding.PrimaryColorDark.Should().Be("#3FBF8F");
        config.Branding.LogoUrl.Should().Contain("logo.png");

        // Endpoints
        config.Endpoints.Should().NotBeNull();
        config.Endpoints!.SalahApiUrl.Should().EndWith("/salahapi");
        config.Endpoints.EventsUrl.Should().EndWith("/events");
        config.Endpoints.AnnouncementsUrl.Should().EndWith("/announcements");

        // Contact
        config.Contact.Should().NotBeNull();
        config.Contact!.Email.Should().Be("info@example.org");

        // Donation URL
        config.DonationUrl.Should().Be("https://example.org/donate");

        // Social
        config.Social.Should().NotBeNull();
        config.Social!.Facebook.Should().Contain("facebook.com");
        config.Social.Instagram.Should().Contain("instagram.com");
        config.Social.WhatsApp.Should().Contain("chat.whatsapp.com");

        // Feature flags
        config.FeatureFlags.Should().NotBeNull();
        config.FeatureFlags!.Events.Should().BeTrue();
        config.FeatureFlags.Donations.Should().BeTrue();
        config.FeatureFlags.Qibla.Should().BeTrue();
        config.FeatureFlags.PrayerReminders.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_PartialJson_DoesNotThrow()
    {
        // API-07: tolerant parsing - partial/minimal payload must not throw.
        var partialJson = """{"masjid":{"id":"test","name":"Test Masjid"}}""";

        var act = () => JsonSerializer.Deserialize(partialJson, MasjidCoreJsonContext.Default.MasjidConfig);

        act.Should().NotThrow();
        var config = act();
        config!.Masjid!.Id.Should().Be("test");
        config.Endpoints.Should().BeNull("missing endpoints field should deserialize as null");
        config.FeatureFlags.Should().BeNull("missing featureFlags should deserialize as null");
    }

    [Fact]
    public void Deserialize_ExtraUnknownFields_DoesNotThrow()
    {
        // API-07: forward compatibility - unknown fields are ignored.
        var jsonWithExtras = """
            {
              "masjid": {"id": "x", "name": "X Masjid"},
              "unknownFutureField": "value",
              "anotherNewField": {"nested": true}
            }
            """;

        var act = () => JsonSerializer.Deserialize(jsonWithExtras, MasjidCoreJsonContext.Default.MasjidConfig);

        act.Should().NotThrow();
        act()!.Masjid!.Id.Should().Be("x");
    }

    [Fact]
    public void Deserialize_EmptyJson_DoesNotThrow()
    {
        // NFR-RL-01: even a completely empty object must not crash.
        var act = () => JsonSerializer.Deserialize("{}", MasjidCoreJsonContext.Default.MasjidConfig);
        act.Should().NotThrow();
        var config = act();
        config.Should().NotBeNull();
        config!.Masjid.Should().BeNull();
    }

    [Fact]
    public void FeatureFlags_MissingFields_DefaultToFalse()
    {
        // Feature flags default to false when absent - hides features unless explicitly enabled.
        var json = """{"featureFlags":{}}""";
        var config = JsonSerializer.Deserialize(json, MasjidCoreJsonContext.Default.MasjidConfig);

        config!.FeatureFlags!.Events.Should().BeFalse();
        config.FeatureFlags.Donations.Should().BeFalse();
    }
}
