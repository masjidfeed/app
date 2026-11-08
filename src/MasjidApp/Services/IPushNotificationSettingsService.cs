namespace MasjidApp.Services;

/// <summary>Controls the app-level remote push opt-in and shared OS notification permission.</summary>
public interface IPushNotificationSettingsService
{
    /// <summary>The persisted notification state on this device.</summary>
    PushNotificationState State { get; }

    /// <summary>
    /// Whether notifications are currently allowed at the OS (device) level.
    /// </summary>
    Task<bool> IsDeviceNotificationsEnabledAsync(CancellationToken ct = default);

    /// <summary>Refreshes permission state and synchronizes the remote registration.</summary>
    Task<PushNotificationState> RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Enables remote push: attempts the registration POST even when device
    /// notifications are disabled (the UI then prompts to enable them).
    /// </summary>
    Task<PushNotificationState> EnableAsync(CancellationToken ct = default);

    /// <summary>
    /// Retries the registration POST when the state is
    /// <see cref="PushNotificationState.EnabledButFailed"/>.
    /// </summary>
    Task<PushNotificationState> RetryRegistrationAsync(CancellationToken ct = default);

    /// <summary>Disables remote push and removes the remote registration.</summary>
    Task DisableAsync(CancellationToken ct = default);
}

/// <summary>Opens the OS page where app notifications can be enabled.</summary>
public interface IDeviceNotificationSettings
{
    /// <summary>Launches the platform notification-settings page for this app.</summary>
    Task OpenAsync();
}
