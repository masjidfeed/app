using System.Text.Json;
using FluentAssertions;
using MasjidApp.Core.SalahApi;

namespace MasjidApp.Core.Tests.ContractTests;

// QA-03: contract tests verify that the live mock/salahapi.json parses into the
// app's SalahApiDocument model.  A drift between the mock and the model causes
// these tests to fail, prompting a fix before it reaches production.

/// <summary>
/// Reads <c>mock/salahapi.json</c> from disk and verifies it deserializes into
/// <see cref="SalahApiDocument"/> with the expected field values.
/// </summary>
public sealed class SalahApiContractTests
{
    [Fact]
    public void SalahApi_MockFile_DeserializesWithoutError()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "salahapi.json"));

        var act = () => JsonSerializer.Deserialize(json, SalahApiJsonContext.Default.SalahApiDocument);

        act.Should().NotThrow();
    }

    [Fact]
    public void SalahApi_MockFile_VersionIs1_0()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "salahapi.json"));

        var doc = JsonSerializer.Deserialize(json, SalahApiJsonContext.Default.SalahApiDocument);

        doc.Should().NotBeNull();
        doc!.Version.Should().Be("1.1");
    }

    [Fact]
    public void SalahApi_MockFile_LocationTimezoneIsLosAngeles()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "salahapi.json"));

        var doc = JsonSerializer.Deserialize(json, SalahApiJsonContext.Default.SalahApiDocument);

        doc!.Location.Should().NotBeNull();
        doc.Location!.Timezone.Should().Be("America/Los_Angeles");
    }

    [Fact]
    public void SalahApi_MockFile_CalculationMethodIsIsna()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "salahapi.json"));

        var doc = JsonSerializer.Deserialize(json, SalahApiJsonContext.Default.SalahApiDocument);

        doc!.CalculationMethod.Should().NotBeNull();
        doc.CalculationMethod!.Name.Should().Be("ISNA");
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
