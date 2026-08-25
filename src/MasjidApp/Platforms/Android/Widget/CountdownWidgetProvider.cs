using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using MasjidApp.Core.Widgets;

namespace MasjidApp.Platforms.Android.Widget;

// TC-04, FR-WG-01, FR-WG-04, FR-WG-05: Android 2x2 next-prayer countdown App Widget.

/// <summary>
/// Renders a live countdown to the next prayer and its iqama time from the shared snapshot.
/// </summary>
[BroadcastReceiver(Label = "Next Prayer Countdown", Exported = true)]
[IntentFilter(["android.appwidget.action.APPWIDGET_UPDATE"])]
[MetaData("android.appwidget.provider", Resource = "@xml/countdown_widget_info")]
public sealed class CountdownWidgetProvider : AppWidgetProvider
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
        var views = new RemoteViews(context.PackageName!, Resource.Layout.widget_countdown);
        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (data is null)
        {
            views.SetTextViewText(Resource.Id.widget_countdown_masjid_name, "Masjid App");
            views.SetTextViewText(Resource.Id.widget_countdown_hijri_date, string.Empty);
            views.SetTextViewText(Resource.Id.widget_countdown_gregorian_date, string.Empty);
            ShowStatus(views, "Open the app to configure your masjid");
            manager.UpdateAppWidget(widgetId, views);
            return;
        }

        var currentDay = WidgetDaySelector.CurrentDay(data, nowEpoch);
        var currentPrayerName = WidgetDaySelector.CurrentPrayerName(data, nowEpoch);
        views.SetInt(
            Resource.Id.widget_countdown_root,
            "setBackgroundResource",
            SmallWidgetSupport.GetBackgroundResource(currentPrayerName));
        views.SetTextViewText(Resource.Id.widget_countdown_masjid_name, data.MasjidName);
        views.SetTextViewText(
            Resource.Id.widget_countdown_hijri_date,
            currentDay is null ? string.Empty : SmallWidgetSupport.CompactHijriDate(currentDay.HijriDate));
        views.SetTextViewText(
            Resource.Id.widget_countdown_gregorian_date,
            currentDay is null
                ? string.Empty
                : SmallWidgetSupport.CompactGregorianDate(currentDay.GregorianDate));

        if (WidgetDaySelector.IsStale(data, nowEpoch))
        {
            ShowStatus(views, "Times may be outdated\nOpen the app to update");
            manager.UpdateAppWidget(widgetId, views);
            return;
        }

        var target = SmallWidgetSupport.FindNextTarget(data, nowEpoch);
        if (target is null)
        {
            ShowStatus(views, "Open the app to update prayer times");
            manager.UpdateAppWidget(widgetId, views);
            return;
        }

        var remainingMilliseconds = Math.Max(0, target.EpochSeconds - nowEpoch) * 1000L;
        var chronometerBase = SystemClock.ElapsedRealtime() + remainingMilliseconds;

        views.SetViewVisibility(Resource.Id.widget_countdown_content, ViewStates.Visible);
        views.SetViewVisibility(Resource.Id.widget_countdown_status, ViewStates.Gone);
        views.SetBoolean(Resource.Id.widget_countdown_timer, "setCountDown", true);
        views.SetChronometer(Resource.Id.widget_countdown_timer, chronometerBase, "%s", true);
        views.SetProgressBar(
            Resource.Id.widget_countdown_progress,
            1000,
            SmallWidgetSupport.RemainingProgress(target, data, nowEpoch),
            false);
        views.SetTextViewText(
            Resource.Id.widget_countdown_label,
            SmallWidgetSupport.CompactCountdownLabel(target));
        views.SetTextViewText(Resource.Id.widget_countdown_prayer, target.Prayer.Name);
        views.SetTextViewText(
            Resource.Id.widget_countdown_iqama,
            SmallWidgetSupport.DisplayTime(target));

        manager.UpdateAppWidget(widgetId, views);
    }

    private static void ShowStatus(RemoteViews views, string message)
    {
        views.SetViewVisibility(Resource.Id.widget_countdown_content, ViewStates.Gone);
        views.SetTextViewText(Resource.Id.widget_countdown_status, message);
        views.SetViewVisibility(Resource.Id.widget_countdown_status, ViewStates.Visible);
    }
}
