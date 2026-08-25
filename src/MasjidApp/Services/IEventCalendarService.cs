using MasjidApp.Core.Events;

namespace MasjidApp.Services;

/// <summary>Presents the platform calendar's add-event editor.</summary>
public interface IEventCalendarService
{
    /// <summary>Opens a prefilled native editor and returns whether it was saved.</summary>
    Task<bool> PresentAddEventAsync(CalendarEventDraft draft, CancellationToken ct = default);
}
