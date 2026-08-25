using FluentAssertions;
using MasjidApp.Core.Events;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Tests.Events;

public sealed class CalendarEventDraftFactoryTests
{
    [Fact]
    public void Create_ConcreteOccurrence_PrefillsNativeCalendarValues()
    {
        var start = new DateTimeOffset(2026, 8, 1, 18, 30, 0, TimeSpan.FromHours(-7));
        var occurrence = new MasjidEventOccurrence(
            new MasjidPost
            {
                Title = "Community Dinner",
                Description = "<p>Bring family &amp; friends &#x1F64F;</p><script>bad()</script>",
                Location = "Main Hall",
                EventDateTime = start,
            },
            start);

        var draft = CalendarEventDraftFactory.Create(occurrence);

        draft.Title.Should().Be("Community Dinner");
        draft.Start.Should().Be(start);
        draft.End.Should().Be(start.AddHours(1));
        draft.Location.Should().Be("Main Hall");
        draft.Notes.Should().Be("Bring family & friends 🙏");
    }
}
