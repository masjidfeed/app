using System.Text.Json;

namespace MasjidApp.Notifications;

/// <summary>Preferences-backed, tenant-isolated remote notification inbox.</summary>
public sealed class NotificationInboxService : INotificationInboxService
{
    private const string StorageKey = "push_notification_inbox";
    private const int MaximumPerMasjid = 100;
    private readonly Lock _sync = new();

    public event EventHandler? Changed;

    public IReadOnlyList<NotificationInboxItem> GetNotifications(string masjidId)
    {
        lock (_sync)
        {
            return Load()
                .Where(item => string.Equals(item.MasjidId, masjidId, StringComparison.Ordinal))
                .OrderByDescending(static item => item.ReceivedAt)
                .ToList();
        }
    }

    public int GetUnreadCount(string masjidId)
    {
        lock (_sync)
        {
            return Load().Count(item =>
                !item.IsRead && string.Equals(item.MasjidId, masjidId, StringComparison.Ordinal));
        }
    }

    public void Upsert(NotificationInboxItem notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_sync)
        {
            var items = Load();
            var existingIndex = items.FindIndex(item =>
                string.Equals(item.NotificationId, notification.NotificationId, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                var existing = items[existingIndex];
                items[existingIndex] = notification with
                {
                    ReceivedAt = existing.ReceivedAt,
                    IsRead = existing.IsRead,
                };
            }
            else
            {
                items.Add(notification);
            }

            var retained = items
                .GroupBy(static item => item.MasjidId, StringComparer.Ordinal)
                .SelectMany(group => group.OrderByDescending(static item => item.ReceivedAt).Take(MaximumPerMasjid))
                .ToList();
            Save(retained);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkAllRead(string masjidId) => UpdateMasjid(
        masjidId,
        static item => item.IsRead ? item : item with { IsRead = true });

    public void MarkRead(string masjidId, string notificationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masjidId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        UpdateMasjid(
            masjidId,
            item => string.Equals(item.NotificationId, notificationId, StringComparison.Ordinal) &&
                    !item.IsRead
                ? item with { IsRead = true }
                : item);
    }

    public void Delete(string masjidId, string notificationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masjidId);
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        lock (_sync)
        {
            var retained = Load()
                .Where(item =>
                    !string.Equals(item.MasjidId, masjidId, StringComparison.Ordinal) ||
                    !string.Equals(item.NotificationId, notificationId, StringComparison.Ordinal))
                .ToList();
            Save(retained);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear(string masjidId)
    {
        lock (_sync)
        {
            var retained = Load()
                .Where(item => !string.Equals(item.MasjidId, masjidId, StringComparison.Ordinal))
                .ToList();
            Save(retained);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateMasjid(
        string masjidId,
        Func<NotificationInboxItem, NotificationInboxItem> update)
    {
        lock (_sync)
        {
            var items = Load();
            for (var index = 0; index < items.Count; index++)
            {
                if (string.Equals(items[index].MasjidId, masjidId, StringComparison.Ordinal))
                {
                    items[index] = update(items[index]);
                }
            }

            Save(items);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<NotificationInboxItem> Load()
    {
        var json = Preferences.Default.Get(StorageKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(
                json,
                NotificationInboxJsonContext.Default.ListNotificationInboxItem) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void Save(List<NotificationInboxItem> items) => Preferences.Default.Set(
        StorageKey,
        JsonSerializer.Serialize(items, NotificationInboxJsonContext.Default.ListNotificationInboxItem));
}