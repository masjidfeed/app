using MasjidApp.Push;
using Plugin.LocalNotification;

namespace MasjidApp.Services;

/// <summary>
/// Persists remote push notification state (disabled / enabled / enabled-but-failed)
/// and coordinates the OS notification permission with FCM registration.
/// </summary>
public sealed class PushNotificationSettingsService : IPushNotificationSettingsService
{
    internal const string StateKey = "settings_push_notifications_state";

    private readonly IUserPreferences _preferences;
    private readonly INotificationService _notifications;
    private readonly IPushNotificationCoordinator _coordinator;

    /// <summary>Creates the remote push settings service.</summary>
    public PushNotificationSettingsService(
        IUserPreferences preferences,
        INotificationService notifications,
        IPushNotificationCoordinator coordinator)
    {
        _preferences = preferences;
        _notifications = notifications;
        _coordinator = coordinator;
    }

    /// <inheritdoc />
    public PushNotificationState State =>
        (PushNotificationState)_preferences.GetValue(StateKey, (int)PushNotificationState.Disabled);

    /// <inheritdoc />
    public async Task<bool> IsDeviceNotificationsEnabledAsync(CancellationToken ct = default)
    {
        try
        {
            return await _notifications.AreNotificationsEnabled().ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<PushNotificationState> RefreshAsync(CancellationToken ct = default)
    {
        var state = State;
        if (state == PushNotificationState.Disabled)
        {
            await _coordinator.SyncAsync(enabled: false, ct: ct).ConfigureAwait(false);
            return PushNotificationState.Disabled;
        }

        var allowed = await IsDeviceNotificationsEnabledAsync(ct).ConfigureAwait(false);
        if (!allowed)
        {
            // The OS revoked notifications; the app-level opt-in cannot be honored.
            SetState(PushNotificationState.Disabled);
            await _coordinator.SyncAsync(enabled: false, ct: ct).ConfigureAwait(false);
            return PushNotificationState.Disabled;
        }

        if (state == PushNotificationState.EnabledButFailed)
        {
            // The user may have enabled notifications at the OS level and returned;
            // retry the registration before settling on the final state.
            return await RetryRegistrationAsync(ct).ConfigureAwait(false);
        }

        await _coordinator.SyncAsync(enabled: true, ct: ct).ConfigureAwait(false);
        return state;
    }

    /// <inheritdoc />
    public async Task<PushNotificationState> EnableAsync(CancellationToken ct = default)
    {
        var allowed = await IsDeviceNotificationsEnabledAsync(ct).ConfigureAwait(false);

        // Attempt the registration POST regardless of the OS permission so the
        // backend is ready as soon as notifications are turned on at the OS level.
        var registered = await _coordinator
            .SyncAsync(enabled: true, forceReRegister: true, ct: ct)
            .ConfigureAwait(false);

        if (!allowed)
        {
            // Device notifications are off: keep the app-level opt-in on; the settings
            // UI prompts the user to enable notifications in system settings.
            SetState(PushNotificationState.EnabledButFailed);
            return PushNotificationState.EnabledButFailed;
        }

        var newState = registered
            ? PushNotificationState.Enabled
            : PushNotificationState.EnabledButFailed;
        SetState(newState);
        return newState;
    }

    /// <inheritdoc />
    public async Task<PushNotificationState> RetryRegistrationAsync(CancellationToken ct = default)
    {
        var registered = await _coordinator
            .SyncAsync(enabled: true, forceReRegister: true, ct: ct)
            .ConfigureAwait(false);
        var newState = registered
            ? PushNotificationState.Enabled
            : PushNotificationState.EnabledButFailed;
        SetState(newState);
        return newState;
    }

    /// <inheritdoc />
    public async Task DisableAsync(CancellationToken ct = default)
    {
        SetState(PushNotificationState.Disabled);
        await _coordinator.SyncAsync(enabled: false, ct: ct).ConfigureAwait(false);
    }

    private void SetState(PushNotificationState state) =>
        _preferences.SetValue(StateKey, (int)state);
}
