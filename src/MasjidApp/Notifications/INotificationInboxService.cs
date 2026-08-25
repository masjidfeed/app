namespace MasjidApp.Notifications;

public interface INotificationInboxService
{
    event EventHandler? Changed;

    IReadOnlyList<NotificationInboxItem> GetNotifications(string masjidId);

    int GetUnreadCount(string masjidId);

    void Upsert(NotificationInboxItem notification);

    void MarkRead(string masjidId, string notificationId);

    void MarkAllRead(string masjidId);

    void Delete(string masjidId, string notificationId);

    void Clear(string masjidId);
}