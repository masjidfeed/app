using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace MasjidApp.Core.Events;

// FR-EV-07: expand iCal RRULE strings to concrete occurrences over a bounded window.

/// <summary>
/// Pure, side-effect-free utility for expanding an iCal RRULE string into concrete
/// <see cref="DateTimeOffset"/> occurrences within a bounded time window.
/// All methods are thread-safe.
/// </summary>
public static class RRuleExpander
{
    /// <summary>
    /// Expands an iCal RRULE string to all occurrences of <paramref name="dtStart"/>
    /// that fall within [<paramref name="windowStart"/>, <paramref name="windowEnd"/>].
    /// </summary>
    /// <param name="rruleString">
    /// An iCal RRULE string (e.g. <c>FREQ=WEEKLY;BYDAY=FR</c>), or <c>null</c>.
    /// </param>
    /// <param name="dtStart">The base start date/time of the series.</param>
    /// <param name="windowStart">Inclusive start of the expansion window.</param>
    /// <param name="windowEnd">Inclusive end of the expansion window.</param>
    /// <returns>
    /// Sorted, read-only list of occurrence date/times; empty when the RRULE is
    /// absent or malformed.  Never throws (NFR-RL-01).
    /// </returns>
    public static IReadOnlyList<DateTimeOffset> Expand(
        string? rruleString,
        DateTimeOffset dtStart,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        if (string.IsNullOrWhiteSpace(rruleString))
        {
            return Array.Empty<DateTimeOffset>();
        }

        try
        {
            var pattern = new RecurrencePattern(rruleString);

            // Use the local time in dtStart's offset so day-of-week expansions
            // (e.g. BYDAY=FR) are computed in the correct local timezone.
            var calDtStart = new CalDateTime(dtStart.DateTime);

            var calEvent = new CalendarEvent
            {
                DtStart = calDtStart,
                RecurrenceRules = new List<RecurrencePattern> { pattern },
            };

            // Convert window bounds to the same offset for an apples-to-apples comparison.
            // Widen the search by 1 second on each side so that occurrences exactly at
            // the window boundaries (including DTSTART) are not excluded by Ical.Net's
            // range evaluation; the .Where() below filters back to the real window.
            var wsLocal = windowStart.ToOffset(dtStart.Offset).DateTime.AddSeconds(-1);
            var weLocal = windowEnd.ToOffset(dtStart.Offset).DateTime.AddSeconds(1);

            var occurrences = calEvent.GetOccurrences(wsLocal, weLocal);

            return occurrences
                .Select(o =>
                {
                    // Strip DateTimeKind before constructing DateTimeOffset: the
                    // DateTimeOffset(DateTime, TimeSpan) ctor throws when Kind is Local/Utc
                    // and the supplied offset does not match the machine timezone.
                    // Ical.Net preserves the numeric value, so stripping the Kind is safe.
                    var unspecified = DateTime.SpecifyKind(o.Period.StartTime.Value, DateTimeKind.Unspecified);
                    return new DateTimeOffset(unspecified, dtStart.Offset);
                })
                .Where(dto => dto >= windowStart && dto <= windowEnd)
                .OrderBy(dto => dto)
                .ToList();
        }
        catch
        {
            // NFR-RL-01: tolerate malformed RRULE - degrade to empty, never throw.
            return Array.Empty<DateTimeOffset>();
        }
    }
}
