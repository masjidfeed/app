using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using MasjidApp.Core.Widgets;

namespace MasjidApp.Platforms.Android.Widget;

// TC-04, FR-WG-01, FR-WG-04, FR-WG-05: Android compact next-prayer App Widget.

/// <summary>
/// Android App Widget provider for the compact prayer widget. Mirrors the iOS
/// medium widget by showing the Hijri date, masjid name, next prayer countdown,
/// progress, and a compact all-prayers strip.
/// </summary>
[BroadcastReceiver(Label = "Prayer Times", Exported = true)]
[IntentFilter(["android.appwidget.action.APPWIDGET_UPDATE"])]
[MetaData("android.appwidget.provider", Resource = "@xml/next_prayer_widget_info")]
public sealed class NextPrayerWidgetProvider : AppWidgetProvider
{
    /// <inheritdoc />
    public override void OnUpdate(
        Context? context,
        AppWidgetManager? appWidgetManager,
        int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null)
        {
            return;
        }

        var data = SmallWidgetSupport.LoadWidgetData(context);
        foreach (var id in appWidgetIds)
        {
            UpdateWidget(context, appWidgetManager, id, data);
        }
    }

    private static void UpdateWidget(
        Context context,
        AppWidgetManager manager,
        int widgetId,
        WidgetData? data)
    {
        var views = new RemoteViews(context.PackageName!, Resource.Layout.widget_next_prayer);
        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (data is null)
        {
            views.SetTextViewText(Resource.Id.widget_next_hijri_date, string.Empty);
            views.SetTextViewText(Resource.Id.widget_next_masjid_name, "Masjid App");
            views.SetTextViewText(Resource.Id.widget_next_countdown, "Open app");
            views.SetTextViewText(Resource.Id.widget_next_countdown_label, "to configure your masjid");
            views.SetProgressBar(Resource.Id.widget_next_progress, 1000, 0, false);
            manager.UpdateAppWidget(widgetId, views);
            return;
        }

        var currentDay = WidgetDaySelector.CurrentDay(data, nowEpoch);
        var currentPrayerName = WidgetDaySelector.CurrentPrayerName(data, nowEpoch);
        var target = SmallWidgetSupport.FindNextTarget(data, nowEpoch);
        var isStale = WidgetDaySelector.IsStale(data, nowEpoch);

        views.SetInt(
            Resource.Id.widget_next_root,
            "setBackgroundResource",
            SmallWidgetSupport.GetBackgroundResource(currentPrayerName));
        views.SetTextViewText(Resource.Id.widget_next_hijri_date, currentDay?.HijriDate ?? string.Empty);
        views.SetTextViewText(Resource.Id.widget_next_masjid_name, data.MasjidName);

        if (isStale)
        {
            views.SetTextViewText(Resource.Id.widget_next_staleness, "Times may be outdated - open the app");
            views.SetViewVisibility(Resource.Id.widget_next_staleness, ViewStates.Visible);
        }
        else
        {
            views.SetViewVisibility(Resource.Id.widget_next_staleness, ViewStates.Gone);
        }

        if (target is null)
        {
            views.SetChronometer(
                Resource.Id.widget_next_countdown,
                SystemClock.ElapsedRealtime(),
                null,
                false);
            views.SetTextViewText(Resource.Id.widget_next_countdown, "Open app");
            views.SetTextViewText(Resource.Id.widget_next_countdown_label, "to update prayer times");
            views.SetProgressBar(Resource.Id.widget_next_progress, 1000, 1000, false);
        }
        else
        {
            var remainingMilliseconds = Math.Max(0, target.EpochSeconds - nowEpoch) * 1000L;
            var chronometerBase = SystemClock.ElapsedRealtime() + remainingMilliseconds;
            views.SetChronometer(
                Resource.Id.widget_next_countdown,
                chronometerBase,
                null,
                true);
            views.SetBoolean(Resource.Id.widget_next_countdown, "setCountDown", true);
            views.SetTextViewText(
                Resource.Id.widget_next_countdown_label,
                SmallWidgetSupport.CountdownLabel(target));
            views.SetProgressBar(
                Resource.Id.widget_next_progress,
                1000,
                SmallWidgetSupport.RemainingProgress(target, data, nowEpoch),
                false);
        }

        SetCompactPrayer(views, "fajr", SmallWidgetSupport.GetPrayer(currentDay, "Fajr"));
        SetCompactPrayer(views, "sunrise", SmallWidgetSupport.GetPrayer(currentDay, "Sunrise"));
        SetCompactPrayer(views, "dhuhr", SmallWidgetSupport.GetPrayer(currentDay, "Dhuhr"));
        SetCompactPrayer(views, "asr", SmallWidgetSupport.GetPrayer(currentDay, "Asr"));
        SetCompactPrayer(views, "maghrib", SmallWidgetSupport.GetPrayer(currentDay, "Maghrib"));
        SetCompactPrayer(views, "isha", SmallWidgetSupport.GetPrayer(currentDay, "Isha"));

        manager.UpdateAppWidget(widgetId, views);
    }

    private static void SetCompactPrayer(RemoteViews views, string prayerKey, WidgetPrayerEntry? entry)
    {
        var athanId = SmallWidgetSupport.GetResourceId($"widget_next_{prayerKey}_athan");
        var iqamaId = SmallWidgetSupport.GetResourceId($"widget_next_{prayerKey}_iqama");
        if (athanId == 0 || iqamaId == 0)
        {
            return;
        }

        views.SetTextViewText(iqamaId, SmallWidgetSupport.DisplayTime(entry));
        views.SetTextViewText(
            athanId,
            string.IsNullOrEmpty(entry?.IqamaTime)
                ? string.Empty
                : SmallWidgetSupport.StripMeridiem(entry.AthanTime));
    }
}
