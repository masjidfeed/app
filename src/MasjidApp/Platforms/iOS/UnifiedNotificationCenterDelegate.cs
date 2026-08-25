using Foundation;
using MasjidApp.Push;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Firebase.CloudMessaging;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Platforms;
using UserNotifications;

namespace MasjidApp.Platforms.iOS;

/// <summary>Dispatches iOS local notifications to the scheduler plugin and remote notifications to Firebase.</summary>
public sealed class UnifiedNotificationCenterDelegate : UserNotificationCenterDelegate
{
    public static UnifiedNotificationCenterDelegate Instance { get; } = new();

    public static void Install() =>
        UNUserNotificationCenter.Current.Delegate = Instance;

    public override void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
    {
        if (LocalNotificationCenter.GetRequest(notification.Request.Content) is not null)
        {
            base.WillPresentNotification(center, notification, completionHandler);
            return;
        }

        FirebaseMessaging.WillPresentNotification(center, notification, completionHandler);
    }

    public override void DidReceiveNotificationResponse(
        UNUserNotificationCenter center,
        UNNotificationResponse response,
        Action completionHandler)
    {
        if (LocalNotificationCenter.GetRequest(response.Notification.Request.Content) is not null)
        {
            base.DidReceiveNotificationResponse(center, response, completionHandler);
            return;
        }

        var content = response.Notification.Request.Content;
        var data = StringData(content.UserInfo);
        if (data.ContainsKey("deepLink") &&
            IPlatformApplication.Current?.Services.GetService<IPushNotificationCoordinator>() is { } coordinator)
        {
            completionHandler();
            _ = coordinator.HandleNotificationTapAsync(
                content.Title,
                content.Body,
                imageUrl: null,
                data);
            return;
        }

        FirebaseMessaging.DidReceiveNotificationResponse(center, response, completionHandler);
    }

    private static Dictionary<string, string> StringData(NSDictionary userInfo)
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

    private static FirebaseCloudMessagingImplementation FirebaseMessaging =>
        (FirebaseCloudMessagingImplementation)CrossFirebaseCloudMessaging.Current;
}