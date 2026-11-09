using System.Text.Json;
using FluentAssertions;
using MasjidApp.Core.Models;
using MasjidApp.Core.Serialization;

namespace MasjidApp.Core.Tests.ContractTests;

// QA-03: contract tests verify that the live mock/config.json parses into the
// app's models without drift.  Failure here means either the mock or the model
// changed and the two are no longer aligned.

/// <summary>
/// Reads <c>mock/config.json</c> from disk and verifies it deserializes correctly
/// into <see cref="MasjidConfig"/>, asserting known field values from the seed file.
/// </summary>
public sealed class MasjidConfigContractTests
{
    [Fact]
    public void Config_MockFile_DeserializesWithoutError()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "config.json"));

        var act = () => JsonSerializer.Deserialize(json, MasjidCoreJsonContext.Default.MasjidConfig);

        act.Should().NotThrow();
    }

    [Fact]
    public void Config_MockFile_MasjidIdentityIsPopulated()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "config.json"));

        var config = JsonSerializer.Deserialize(json, MasjidCoreJsonContext.Default.MasjidConfig);

        config.Should().NotBeNull();
        config!.Masjid.Should().NotBeNull();

        // The mock tenant identity is deliberately swappable (e.g. "icob2") so the
        // non-ICOB branding path can be exercised, so assert shape rather than value.
        config.Masjid!.Id.Should().NotBeNullOrWhiteSpace();
        config.Masjid.Name.Should().NotBeNullOrWhiteSpace();
        config.Masjid.Timezone.Should().Be("America/Los_Angeles");
    }

    [Fact]
    public void Config_MockFile_EndpointsPresent()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "config.json"));

        var config = JsonSerializer.Deserialize(json, MasjidCoreJsonContext.Default.MasjidConfig);

        config!.Endpoints.Should().NotBeNull();
        config.Endpoints!.SalahApiUrl.Should().NotBeNullOrEmpty();
        config.Endpoints.EventsUrl.Should().NotBeNullOrEmpty();
        config.Endpoints.AnnouncementsUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Config_MockFile_DonationUrlPresent()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "config.json"));

        var config = JsonSerializer.Deserialize(json, MasjidCoreJsonContext.Default.MasjidConfig);

        config!.DonationUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Config_MockFile_PushConfigurationIsPopulated()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "config.json"));

        var config = JsonSerializer.Deserialize(json, MasjidCoreJsonContext.Default.MasjidConfig);

        config!.PushNotifications.Should().NotBeNull();
        config.PushNotifications!.RegistrationUrl.Should().NotBeNullOrWhiteSpace();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetMockDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mock")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir is null
            ? throw new DirectoryNotFoundException("Could not locate 'mock/' directory walking up from test assembly.")
            : Path.Combine(dir, "mock");
    }
}
