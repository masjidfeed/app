namespace MasjidApp.Push;

/// <summary>Coordinates per-tenant Firebase initialization, token registration, and push taps.</summary>
public interface IPushNotificationCoordinator
{
    /// <summary>Registers or unregisters remote push to match the shared reminder opt-in.</summary>
    Task SyncAsync(bool enabled, CancellationToken ct = default);

    /// <summary>Stores and routes one tapped remote notification payload.</summary>
    Task HandleNotificationTapAsync(
        string? title,
        string? body,
        string? imageUrl,
        IDictionary<string, string> data,
        CancellationToken ct = default);
}