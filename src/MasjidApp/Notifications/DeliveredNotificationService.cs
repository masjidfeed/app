#if IOS
using Microsoft.Maui.Platform;
#endif

namespace MasjidApp.Notifications;

public sealed class DeliveredNotificationService : IDeliveredNotificationService
{
    public async Task<IReadOnlyList<NotificationInboxItem>> GetDeliveredAsync(
        CancellationToken ct = default)
    {
#if IOS
        var completion = new TaskCompletionSource<UserNotifications.UNNotification[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => completion.TrySetCanceled(ct));
        UserNotifications.UNUserNotificationCenter.Current.GetDeliveredNotifications(
            notifications => completion.TrySetResult(notifications));
        var delivered = await completion.Task.ConfigureAwait(false);
        return delivered
            .Select(TryCreateIosItem)
            .OfType<NotificationInboxItem>()
            .ToList();
#elif ANDROID
        var manager = Android.App.Application.Context.GetSystemService(Android.Content.Context.NotificationService)
            as Android.App.NotificationManager;
        return manager?.GetActiveNotifications()?
            .Select(TryCreateAndroidItem)
            .OfType<NotificationInboxItem>()
            .ToList() ?? [];
#else
        await Task.CompletedTask;
        return [];
#endif
    }

    public void ClearAll()
    {
#if IOS
        UserNotifications.UNUserNotificationCenter.Current.RemoveAllDeliveredNotifications();
#elif ANDROID
        var manager = Android.App.Application.Context.GetSystemService(Android.Content.Context.NotificationService)
            as Android.App.NotificationManager;
        manager?.CancelAll();
#endif
    }

#if IOS
    private static NotificationInboxItem? TryCreateIosItem(
        UserNotifications.UNNotification notification)
    {
        var content = notification.Request.Content;
        var data = StringData(content.UserInfo);
        return TryCreateItem(
            content.Title,
            content.Body,
            imageUrl: null,
            data,
            notification.Date is null
                ? DateTimeOffset.UtcNow
                : new DateTimeOffset(notification.Date.ToDateTime()));
    }

    private static Dictionary<string, string> StringData(Foundation.NSDictionary userInfo)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in userInfo.Keys)
        {
            var name = key.ToString();
            if (string.IsNullOrWhiteSpace(name) ||
                string.Equals(name, "aps", StringComparison.Ordinal) ||
                string.Equals(name, "fcm_options", StringComparison.Ordinal))
            {
                continue;
            }

            var value = userInfo.ObjectForKey(key)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                data[name] = value;
            }
        }

        return data;
    }
#elif ANDROID
    private static NotificationInboxItem? TryCreateAndroidItem(
        Android.Service.Notification.StatusBarNotification delivered)
    {
        var notification = delivered.Notification;
        var extras = notification?.Extras;
        if (extras is null)
        {
            return null;
        }

        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[] { "notificationId", "masjidId", "postId", "type", "deepLink" })
        {
            var value = extras.GetString(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                data[key] = value;
            }
        }

        return TryCreateItem(
            extras.GetCharSequence(Android.App.Notification.ExtraTitle)?.ToString(),
            extras.GetCharSequence(Android.App.Notification.ExtraText)?.ToString(),
            imageUrl: null,
            data,
            DateTimeOffset.FromUnixTimeMilliseconds(delivered.PostTime));
    }
#endif

    private static NotificationInboxItem? TryCreateItem(
        string? title,
        string? body,
        string? imageUrl,
        Dictionary<string, string> data,
        DateTimeOffset receivedAt)
    {
        if (!data.TryGetValue("notificationId", out var notificationId) ||
            !data.TryGetValue("masjidId", out var masjidId) ||
            string.IsNullOrWhiteSpace(notificationId) ||
            string.IsNullOrWhiteSpace(masjidId))
        {
            return null;
        }

        int? postId = data.TryGetValue("postId", out var postIdValue) &&
            int.TryParse(postIdValue, out var parsedPostId)
                ? parsedPostId
                : null;
        data.TryGetValue("deepLink", out var deepLink);
        data.TryGetValue("type", out var type);
        return new NotificationInboxItem(
            notificationId,
            masjidId,
            title ?? string.Empty,
            body ?? string.Empty,
            imageUrl,
            deepLink,
            type,
            postId,
            receivedAt,
            IsRead: false);
    }
}