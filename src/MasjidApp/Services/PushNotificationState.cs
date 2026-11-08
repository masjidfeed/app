namespace MasjidApp.Services;

/// <summary>Persisted state of remote announcement notifications on this device.</summary>
public enum PushNotificationState
{
    /// <summary>The user turned announcement notifications off.</summary>
    Disabled = 0,

    /// <summary>Notifications are enabled and the registration POST succeeded.</summary>
    Enabled = 1,

    /// <summary>The user wants notifications on, but registration to the backend failed.</summary>
    EnabledButFailed = 2,
}
