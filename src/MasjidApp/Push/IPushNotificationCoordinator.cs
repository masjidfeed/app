namespace MasjidApp.Push;

/// <summary>Coordinates Firebase initialization, topic registration, and push taps.</summary>
public interface IPushNotificationCoordinator
{
    /// <summary>
    /// Registers or unregisters remote push to match the shared reminder opt-in.
    /// Returns <c>true</c> when the desired end state was reached: the registration
    /// POST succeeded (or is already up-to-date) when enabled, or the unregistration
    /// succeeded when disabled. Failures return <c>false</c>.
    /// </summary>
    Task<bool> SyncAsync(bool enabled, bool forceReRegister = false, CancellationToken ct = default);

    /// <summary>Stores and routes one tapped remote notification payload.</summary>
    Task HandleNotificationTapAsync(
        string? title,
        string? body,
        string? imageUrl,
        IDictionary<string, string> data,
        CancellationToken ct = default);
}
