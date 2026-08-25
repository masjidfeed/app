using System.Text.Json;
using FluentAssertions;
using MasjidApp.Core.Announcements;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Tests.ContractTests;

// QA-03: contract tests verify that the live mock/announcements.json parses into
// the app's unified post model list. Two announcements are in the seed file;
// the first has a non-null description field.

/// <summary>
/// Reads <c>mock/announcements.json</c> from disk and verifies it deserializes
/// into a <see cref="List{MasjidPost}"/> with the expected shape.
/// </summary>
public sealed class AnnouncementsContractTests
{
    [Fact]
    public void Announcements_MockFile_DeserializesWithoutError()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "announcements.json"));

        var act = () => JsonSerializer.Deserialize(json, AnnouncementsJsonContext.Default.ListMasjidPost);

        act.Should().NotThrow();
    }

    [Fact]
    public void Announcements_MockFile_Returns2Announcements()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "announcements.json"));

        var announcements = JsonSerializer.Deserialize(json, AnnouncementsJsonContext.Default.ListMasjidPost);

        announcements.Should().NotBeNull();
        announcements!.Should().HaveCount(2);
    }

    [Fact]
    public void Announcements_MockFile_FirstAnnouncementHasDescription()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "announcements.json"));

        var announcements = JsonSerializer.Deserialize(json, AnnouncementsJsonContext.Default.ListMasjidPost);

        announcements![0].Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Announcements_MockFile_AllAnnouncementsHaveTitle()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "announcements.json"));

        var announcements = JsonSerializer.Deserialize(json, AnnouncementsJsonContext.Default.ListMasjidPost);

        announcements!.Should().AllSatisfy(a => a.Title.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public void Announcements_MockFile_ContainsDocumentedLinks()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "announcements.json"));

        var announcements = JsonSerializer.Deserialize(json, AnnouncementsJsonContext.Default.ListMasjidPost);

        announcements!.Should().AllSatisfy(a =>
        {
            a.PostId.Should().BePositive();
            a.DetailsUrl.Should().NotBeNullOrEmpty();
            a.Url.Should().NotBeNullOrEmpty();
        });
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
