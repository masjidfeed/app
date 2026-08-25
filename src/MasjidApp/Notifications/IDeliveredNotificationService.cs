namespace MasjidApp.Notifications;

/// <summary>Reads and clears remote notifications currently displayed by the operating system.</summary>
public interface IDeliveredNotificationService
{
    /// <summary>Returns delivered remote pushes that contain the app's documented data payload.</summary>
    Task<IReadOnlyList<NotificationInboxItem>> GetDeliveredAsync(CancellationToken ct = default);

    void ClearAll();
}