using System.Text.Json;
using FluentAssertions;
using MasjidApp.Core.Events;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Tests.ContractTests;

// QA-03: contract tests verify that the live mock/events.json parses into the
// app's unified post model list. Three events are defined in the seed file;
// the first has a recurrenceRule.

/// <summary>
/// Reads <c>mock/events.json</c> from disk and verifies it deserializes into a
/// <see cref="List{MasjidPost}"/> with the expected shape.
/// </summary>
public sealed class EventsContractTests
{
    [Fact]
    public void Events_MockFile_DeserializesWithoutError()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "events.json"));

        var act = () => JsonSerializer.Deserialize(json, EventsJsonContext.Default.ListMasjidPost);

        act.Should().NotThrow();
    }

    [Fact]
    public void Events_MockFile_Returns3Events()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "events.json"));

        var events = JsonSerializer.Deserialize(json, EventsJsonContext.Default.ListMasjidPost);

        events.Should().NotBeNull();
        events!.Should().HaveCount(3);
    }

    [Fact]
    public void Events_MockFile_FirstEventHasRecurrenceRule()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "events.json"));

        var events = JsonSerializer.Deserialize(json, EventsJsonContext.Default.ListMasjidPost);

        events![0].RecurrenceRule.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Events_MockFile_FirstEventHasTimedAlert()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "events.json"));

        var events = JsonSerializer.Deserialize(json, EventsJsonContext.Default.ListMasjidPost);

        events![0].Alert.Should().NotBeNullOrWhiteSpace();
        events[0].AlertEndDateTime.Should().NotBeNull();
    }

    [Fact]
    public void Events_MockFile_AllEventsHaveNonEmptyTitle()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "events.json"));

        var events = JsonSerializer.Deserialize(json, EventsJsonContext.Default.ListMasjidPost);

        events!.Should().AllSatisfy(e => e.Title.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public void Events_MockFile_ContainsDocumentedLinks()
    {
        var json = File.ReadAllText(Path.Combine(GetMockDir(), "events.json"));

        var events = JsonSerializer.Deserialize(json, EventsJsonContext.Default.ListMasjidPost);

        events!.Should().AllSatisfy(e =>
        {
            e.PostId.Should().BePositive();
            e.DetailsUrl.Should().NotBeNullOrEmpty();
            e.Url.Should().NotBeNullOrEmpty();
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
