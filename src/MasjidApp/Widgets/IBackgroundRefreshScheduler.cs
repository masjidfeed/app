namespace MasjidApp.Widgets;

// TC-04, FR-WG-04: schedules periodic OS background work that regenerates the
// widget snapshot so it stays fresh (countdown advances, day/Hijri date rolls
// over, next-day prayers appear) without the user opening the app.

/// <summary>
/// Abstraction over the platform background scheduler (iOS <c>BGTaskScheduler</c>,
/// Android <c>JobScheduler</c>) that periodically invokes
/// <see cref="WidgetRefreshService.RefreshAsync"/>.  Implementations respect the
/// OS minimum interval and power budget (FR-WG-04); on unsupported platforms the
/// implementation is a no-op.
/// </summary>
public interface IBackgroundRefreshScheduler
{
    /// <summary>
    /// Registers the OS background-task handler.  Must be called once during app
    /// launch (on iOS this has to happen before launch completes).
    /// </summary>
    void Register();

    /// <summary>
    /// Requests the next background run.  Safe to call repeatedly; the OS
    /// coalesces requests and enforces its own minimum interval.
    /// </summary>
    void Schedule();
}
