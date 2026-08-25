using MasjidApp.Core.Utils;

namespace MasjidApp.Core.Events;

/// <summary>Platform-neutral values used to prefill a native calendar event editor.</summary>
public sealed record CalendarEventDraft(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Location,
    string Notes);

/// <summary>Creates calendar drafts from the concrete event occurrence selected by the user.</summary>
public static class CalendarEventDraftFactory
{
    /// <summary>Creates a one-hour draft for one concrete occurrence.</summary>
    public static CalendarEventDraft Create(MasjidEventOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        return new CalendarEventDraft(
            occurrence.Event.Title,
            occurrence.OccurrenceDateTime,
            occurrence.OccurrenceDateTime.AddHours(1),
            occurrence.Event.Location ?? string.Empty,
            HtmlSanitizer.ToPlainText(occurrence.Event.Description));
    }
}
