using System.Text.Json;
using MasjidApp.Core.Widgets;

namespace MasjidApp.Widgets;

// TC-04, FR-WG-01, FR-WG-05: platform-specific shared-container writer.
// iOS: App Group file + UserDefaults fallback (group.com.goodsoftware.masjidapp).
// Android: SharedPreferences "widget_data" + broadcast for widget update.

/// <summary>
/// Writes the <see cref="WidgetData"/> JSON snapshot to the platform-specific
/// shared container so the native widget process can read it without network access
/// (FR-WG-05).
/// </summary>
public sealed class WidgetDataWriter : IWidgetDataWriter
{
    /// <inheritdoc />
    public async Task WriteAsync(WidgetData data, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(data, WidgetDataJsonContext.Default.WidgetData);

#if IOS
        // Write to the App Group shared container so WidgetKit can read the
        // snapshot directly, avoiding UserDefaults caching between processes.
        var containerUrl = Foundation.NSFileManager.DefaultManager.GetContainerUrl("group.com.goodsoftware.masjidapp");
        if (containerUrl?.Path is { Length: > 0 } containerPath)
        {
            var filePath = Path.Combine(containerPath, "widget_data.json");
            var tempPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
        }

        var defaults = new Foundation.NSUserDefaults("group.com.goodsoftware.masjidapp", Foundation.NSUserDefaultsType.SuiteName);
        defaults.SetString(json, "widget_data");
        defaults.Synchronize();

        WidgetTimelineReloader.ReloadAll();
#elif ANDROID
        // Write to private SharedPreferences; the App Widget provider reads the same key.
        var context = Android.App.Application.Context;
        var prefs = context
            .GetSharedPreferences("widget_data", Android.Content.FileCreationMode.Private);
        prefs!.Edit()!.PutString("widget_data_json", json)?.Commit();

        // Broadcast APPWIDGET_UPDATE targeted at our provider and carrying the
        // current widget instance ids so AppWidgetProvider.OnReceive actually
        // dispatches to OnUpdate (a bare ACTION_APPWIDGET_UPDATE with no
        // EXTRA_APPWIDGET_IDS is ignored). This refreshes all instances
        // immediately within the OS throttle window (FR-WG-04).
        var manager = Android.Appwidget.AppWidgetManager.GetInstance(context);
        RefreshAndroidWidget<MasjidApp.Platforms.Android.Widget.NextPrayerWidgetProvider>(context, manager);
        RefreshAndroidWidget<MasjidApp.Platforms.Android.Widget.PrayerTimesWidgetProvider>(context, manager);
        RefreshAndroidWidget<MasjidApp.Platforms.Android.Widget.IqamaTimesWidgetProvider>(context, manager);
        RefreshAndroidWidget<MasjidApp.Platforms.Android.Widget.CountdownWidgetProvider>(context, manager);
#endif

        await Task.CompletedTask.ConfigureAwait(false);
    }

#if ANDROID
    private static void RefreshAndroidWidget<TProvider>(
        Android.Content.Context context,
        Android.Appwidget.AppWidgetManager? manager)
    {
        var component = new Android.Content.ComponentName(
            context,
            Java.Lang.Class.FromType(typeof(TProvider)));
        var ids = manager!.GetAppWidgetIds(component);
        if (ids is not { Length: > 0 })
        {
            return;
        }

        var intent = new Android.Content.Intent(Android.Appwidget.AppWidgetManager.ActionAppwidgetUpdate);
        intent.SetComponent(component);
        intent.PutExtra(Android.Appwidget.AppWidgetManager.ExtraAppwidgetIds, ids);
        context.SendBroadcast(intent);
    }
#endif
}
