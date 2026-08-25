namespace MasjidApp.Core.Widgets;

// TC-04, FR-WG-01, FR-WG-05: contract for writing the render-ready widget snapshot
// to the platform-specific shared container.

/// <summary>
/// Writes the render-ready <see cref="WidgetData"/> snapshot to the platform-specific
/// shared container (iOS App Group / Android SharedPreferences) so the native widget
/// process can read it without network access.
/// </summary>
public interface IWidgetDataWriter
{
    /// <summary>
    /// Serialises <paramref name="data"/> and writes it atomically to the shared container.
    /// The native widget process should be notified to reload after the write completes.
    /// </summary>
    /// <param name="data">The pre-resolved widget snapshot to persist.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task WriteAsync(WidgetData data, CancellationToken ct = default);
}
