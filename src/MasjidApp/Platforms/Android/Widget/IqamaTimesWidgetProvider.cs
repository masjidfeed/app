using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Views;
using Android.Widget;
using MasjidApp.Core.Widgets;

namespace MasjidApp.Platforms.Android.Widget;

// TC-04, FR-WG-01, FR-WG-04, FR-WG-05: Android 2x2 daily iqama App Widget.

/// <summary>
/// Renders today's iqama times as a compact vertical list from the shared snapshot.
/// </summary>
[BroadcastReceiver(Label = "Today's Iqama Times", Exported = true)]
[IntentFilter(["android.appwidget.action.APPWIDGET_UPDATE"])]
[MetaData("android.appwidget.provider", Resource = "@xml/iqama_times_widget_info")]
public sealed class IqamaTimesWidgetProvider : AppWidgetProvider
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
        var views = new RemoteViews(context.PackageName!, Resource.Layout.widget_iqama_times);
        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (data is null)
        {
            views.SetTextViewText(Resource.Id.widget_iqama_masjid_name, "Masjid App");
            views.SetTextViewText(Resource.Id.widget_iqama_date, string.Empty);
            views.SetViewVisibility(Resource.Id.widget_iqama_rows, ViewStates.Gone);
            views.SetTextViewText(Resource.Id.widget_iqama_empty, "Open the app to configure your masjid");
            views.SetViewVisibility(Resource.Id.widget_iqama_empty, ViewStates.Visible);
            manager.UpdateAppWidget(widgetId, views);
            return;
        }

        var currentDay = WidgetDaySelector.CurrentDay(data, nowEpoch);
        var currentPrayerName = WidgetDaySelector.CurrentPrayerName(data, nowEpoch);
        var currentPrayerPeriodName = WidgetDaySelector.CurrentPrayerPeriodName(data, nowEpoch);
        views.SetInt(
            Resource.Id.widget_iqama_root,
            "setBackgroundResource",
            SmallWidgetSupport.GetBackgroundResource(currentPrayerName));
        views.SetTextViewText(Resource.Id.widget_iqama_masjid_name, data.MasjidName);
        views.SetTextViewText(
            Resource.Id.widget_iqama_date,
            currentDay is null
                ? string.Empty
                : $"{SmallWidgetSupport.CompactHijriDate(currentDay.HijriDate)} • " +
                  SmallWidgetSupport.CompactGregorianDate(currentDay.GregorianDate));

        if (WidgetDaySelector.IsStale(data, nowEpoch))
        {
            views.SetViewVisibility(Resource.Id.widget_iqama_rows, ViewStates.Gone);
            views.SetTextViewText(
                Resource.Id.widget_iqama_empty,
                "Times may be outdated\nOpen the app to update");
            views.SetViewVisibility(Resource.Id.widget_iqama_empty, ViewStates.Visible);
        }
        else
        {
            views.SetViewVisibility(Resource.Id.widget_iqama_rows, ViewStates.Visible);
            views.SetViewVisibility(Resource.Id.widget_iqama_empty, ViewStates.Gone);
        }

        SetPrayer(views, "fajr", SmallWidgetSupport.GetPrayer(currentDay, "Fajr"), currentPrayerPeriodName);
        SetPrayer(views, "sunrise", SmallWidgetSupport.GetPrayer(currentDay, "Sunrise"), currentPrayerPeriodName);
        SetPrayer(views, "dhuhr", SmallWidgetSupport.GetPrayer(currentDay, "Dhuhr"), currentPrayerPeriodName);
        SetPrayer(views, "asr", SmallWidgetSupport.GetPrayer(currentDay, "Asr"), currentPrayerPeriodName);
        SetPrayer(views, "maghrib", SmallWidgetSupport.GetPrayer(currentDay, "Maghrib"), currentPrayerPeriodName);
        SetPrayer(views, "isha", SmallWidgetSupport.GetPrayer(currentDay, "Isha"), currentPrayerPeriodName);

        manager.UpdateAppWidget(widgetId, views);
    }

    private static void SetPrayer(
        RemoteViews views,
        string prayerKey,
        WidgetPrayerEntry? prayer,
        string? currentPrayerName)
    {
        var nameId = SmallWidgetSupport.GetResourceId($"widget_iqama_{prayerKey}_name");
        var timeId = SmallWidgetSupport.GetResourceId($"widget_iqama_{prayerKey}");
        var iconId = SmallWidgetSupport.GetResourceId($"widget_iqama_{prayerKey}_icon");
        if (nameId == 0 || timeId == 0 || iconId == 0)
        {
            return;
        }

        var isCurrent = prayer is not null &&
                string.Equals(prayer.Name, currentPrayerName, StringComparison.OrdinalIgnoreCase);
        var highlight = new global::Android.Graphics.Color(unchecked((int)0xFFFFCC00u));
        var normalName = new global::Android.Graphics.Color(unchecked((int)0xFFD1FFFFFFu));
        var normalTime = new global::Android.Graphics.Color(unchecked((int)0xFFFFFFFFu));
        var iconColor = isCurrent ? highlight : normalName;

        views.SetTextViewText(timeId, SmallWidgetSupport.DisplayTime(prayer));
        views.SetTextColor(nameId, isCurrent ? highlight : normalName);
        views.SetTextColor(timeId, isCurrent ? highlight : normalTime);
        views.SetInt(iconId, "setColorFilter", iconColor.ToArgb());
    }
}
