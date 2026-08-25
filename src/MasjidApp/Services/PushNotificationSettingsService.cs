using MasjidApp.Push;
using Microsoft.Maui.ApplicationModel;
using Plugin.LocalNotification;

namespace MasjidApp.Services;

/// <summary>Persists remote push consent and coordinates platform permission with FCM registration.</summary>
public sealed class PushNotificationSettingsService : IPushNotificationSettingsService
{
    internal const string EnabledKey = "settings_push_notifications";

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
    public bool IsEnabled => _preferences.GetValue(EnabledKey, false);

    /// <inheritdoc />
    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            await _coordinator.SyncAsync(enabled: false, ct).ConfigureAwait(false);
            return false;
        }

        var allowed = await _notifications.AreNotificationsEnabled().ConfigureAwait(false);
        if (!allowed)
        {
            _preferences.SetValue(EnabledKey, false);
        }

        await _coordinator.SyncAsync(allowed, ct).ConfigureAwait(false);
        return allowed;
    }

    /// <inheritdoc />
    public async Task<bool> EnableAsync(CancellationToken ct = default)
    {
        var allowed = await _notifications.AreNotificationsEnabled().ConfigureAwait(false);
        if (!allowed)
        {
            allowed = await MainThread.InvokeOnMainThreadAsync(
                () => _notifications.RequestNotificationPermission());
        }

        _preferences.SetValue(EnabledKey, allowed);
        await _coordinator.SyncAsync(allowed, ct).ConfigureAwait(false);
        return allowed;
    }

    /// <inheritdoc />
    public async Task DisableAsync(CancellationToken ct = default)
    {
        _preferences.SetValue(EnabledKey, false);
        await _coordinator.SyncAsync(enabled: false, ct).ConfigureAwait(false);
    }
}
