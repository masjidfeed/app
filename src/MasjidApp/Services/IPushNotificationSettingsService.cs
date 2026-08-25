namespace MasjidApp.Services;

/// <summary>Controls the app-level remote push opt-in and shared OS notification permission.</summary>
public interface IPushNotificationSettingsService
{
    /// <summary>Whether remote push is enabled by the user in the app.</summary>
    bool IsEnabled { get; }

    /// <summary>Refreshes permission state and synchronizes the remote registration.</summary>
    Task<bool> RefreshAsync(CancellationToken ct = default);

    /// <summary>Requests notification permission and enables remote push when granted.</summary>
    Task<bool> EnableAsync(CancellationToken ct = default);

    /// <summary>Disables remote push and removes the remote registration.</summary>
    Task DisableAsync(CancellationToken ct = default);
}
