using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using MasjidApp.Push;
using MasjidApp.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Firebase.CloudMessaging;

namespace MasjidApp;

[Activity(Name = "com.goodsoftware.masjidapp.MainActivity", Theme = "@style/Maui.SplashTheme", Exported = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.Portrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "masjidapp",
    DataHost = "open")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // D1, FR-WG-04: schedule periodic background regeneration of the widget snapshot.
        var scheduler = IPlatformApplication.Current?.Services
            .GetService<IBackgroundRefreshScheduler>();
        scheduler?.Register();
        scheduler?.Schedule();

        HandleNotificationIntent(Intent);
        CreatePushNotificationChannel();
    }

    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is not null)
        {
            HandleNotificationIntent(intent);
        }
    }

    private static void HandleNotificationIntent(Android.Content.Intent? intent)
    {
        FirebaseCloudMessagingImplementation.OnNewIntent(intent);
        var extras = intent?.Extras;
        if (extras is null)
        {
            return;
        }

        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[] { "notificationId", "masjidId", "postId", "type", "deepLink" })
        {
            var value = extras.Get(key)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                data[key] = value;
            }
        }

        if (!data.ContainsKey("deepLink") ||
            IPlatformApplication.Current?.Services.GetService<IPushNotificationCoordinator>() is not { } coordinator)
        {
            return;
        }

        _ = coordinator.HandleNotificationTapAsync(
            NotificationValue(extras, "gcm.n.title", "gcm.notification.title", "title"),
            NotificationValue(extras, "gcm.n.body", "gcm.notification.body", "body"),
            NotificationValue(extras, "gcm.n.image", "gcm.notification.image"),
            data);
    }

    private static string? NotificationValue(Bundle extras, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = extras.Get(key)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private void CreatePushNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var channelId = $"{PackageName}.community";
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(
            channelId,
            "Community updates",
            NotificationImportance.Default));
        FirebaseCloudMessagingImplementation.ChannelId = channelId;
    }
}
