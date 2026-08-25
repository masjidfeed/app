using MasjidApp.Core.Models;

namespace MasjidApp.Core.Events;

/// <summary>
/// A concrete, expanded occurrence of an event post at a specific
/// date/time.  Recurring events produce one instance per expansion hit; non-recurring
/// events produce exactly one instance.
/// </summary>
/// <param name="Event">The source event definition.</param>
/// <param name="OccurrenceDateTime">
/// The concrete date/time of this occurrence, preserving the original UTC offset.
/// </param>
/// <param name="ActiveAlert">
/// The event update that was active when this occurrence was produced, or <c>null</c>.
/// </param>
public sealed record MasjidEventOccurrence(
    MasjidPost Event,
    DateTimeOffset OccurrenceDateTime,
    string? ActiveAlert = null)
{
    /// <summary>Whether this occurrence has an active update to display.</summary>
    public bool HasActiveAlert => !string.IsNullOrWhiteSpace(ActiveAlert);
}
