using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using MasjidApp.Core.Widgets;

namespace MasjidApp.Platforms.Android.Widget;

// TC-04, FR-WG-01, FR-WG-02, FR-WG-04, FR-WG-05: Android App Widget provider.
// Renders all five daily prayers (Fajr, Dhuhr, Asr, Maghrib, Isha) with athan and
// iqama times, highlighting the current prayer.

/// <summary>
/// Android App Widget provider for the Masjid prayer-times widget.
/// Reads the <see cref="WidgetData"/> JSON from the app's private SharedPreferences
/// and renders all today's prayers with athan/iqama times to all registered widget
/// instances on the home screen.
/// </summary>
[BroadcastReceiver(Label = "Prayer Times Widget", Exported = true)]
[IntentFilter(["android.appwidget.action.APPWIDGET_UPDATE"])]
[MetaData("android.appwidget.provider", Resource = "@xml/prayer_widget_info")]
public class PrayerTimesWidgetProvider : AppWidgetProvider
{
    /// <summary>
    /// Called by the OS when one or more widget instances need to be refreshed.
    /// </summary>
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
        AppWidgetManager mgr,
        int widgetId,
        WidgetData? data)
    {
        var views = new RemoteViews(
            context.PackageName!,
            Resource.Layout.widget_prayer_times);

        // Resolve the current day, current prayer, and staleness from the seven-day snapshot
        // using the same shared logic the iOS widget mirrors, so both platforms stay in
        // parity (FR-WG-01, FR-WG-06).
        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var currentDay = data is null ? null : WidgetDaySelector.CurrentDay(data, nowEpoch);
        var currentPrayerName = data is null ? null : WidgetDaySelector.CurrentPrayerName(data, nowEpoch);
        var currentPrayerPeriodName = data is null ? null : WidgetDaySelector.CurrentPrayerPeriodName(data, nowEpoch);
        var isStale = data is not null && WidgetDaySelector.IsStale(data, nowEpoch);

        views.SetInt(
            Resource.Id.widget_root,
            "setBackgroundResource",
            SmallWidgetSupport.GetBackgroundResource(currentPrayerName));

        // Header
        views.SetTextViewText(Resource.Id.widget_masjid_name, data?.MasjidName ?? "Masjid App");
        views.SetTextViewText(Resource.Id.widget_hijri_date, currentDay?.HijriDate ?? string.Empty);
        views.SetTextViewText(Resource.Id.widget_gregorian_date, currentDay?.GregorianDate ?? string.Empty);

        // Staleness banner (FR-WG-01): shown only once "now" has run past the covered
        // window; the last covered day's prayers remain visible underneath.
        if (isStale)
        {
            views.SetTextViewText(
                Resource.Id.widget_staleness,
                "Prayer times may be outdated - open the app to update.");
            views.SetViewVisibility(Resource.Id.widget_staleness, ViewStates.Visible);
        }
        else
        {
            views.SetViewVisibility(Resource.Id.widget_staleness, ViewStates.Gone);
        }

        SetPrayerRow(views, "fajr", SmallWidgetSupport.GetPrayer(currentDay, "Fajr"), currentPrayerPeriodName);
        SetPrayerRow(views, "sunrise", SmallWidgetSupport.GetPrayer(currentDay, "Sunrise"), currentPrayerPeriodName);
        SetPrayerRow(views, "dhuhr", SmallWidgetSupport.GetPrayer(currentDay, "Dhuhr"), currentPrayerPeriodName);
        SetPrayerRow(views, "asr", SmallWidgetSupport.GetPrayer(currentDay, "Asr"), currentPrayerPeriodName);
        SetPrayerRow(views, "maghrib", SmallWidgetSupport.GetPrayer(currentDay, "Maghrib"), currentPrayerPeriodName);
        SetPrayerRow(views, "isha", SmallWidgetSupport.GetPrayer(currentDay, "Isha"), currentPrayerPeriodName);

        mgr.UpdateAppWidget(widgetId, views);
    }

    private static void SetPrayerRow(
        RemoteViews views,
        string prayerKey,
        WidgetPrayerEntry? entry,
        string? currentPrayerName)
    {
        var isCurrent = entry is not null &&
                        string.Equals(entry.Name, currentPrayerName, StringComparison.OrdinalIgnoreCase);
        var highlightColor = new global::Android.Graphics.Color(unchecked((int)0xFFFFCC00u));
        var normalColor = new global::Android.Graphics.Color(unchecked((int)0xFFFFFFFFu));
        var secondaryColor = new global::Android.Graphics.Color(unchecked((int)0xFFC7FFFFFFu));

        var athanId = SmallWidgetSupport.GetResourceId($"widget_{prayerKey}_athan");
        var iqamaId = SmallWidgetSupport.GetResourceId($"widget_{prayerKey}_iqama");
        var nameId = SmallWidgetSupport.GetResourceId($"widget_{prayerKey}_name");
        var iconId = SmallWidgetSupport.GetResourceId($"widget_{prayerKey}_icon");

        if (athanId == 0 || iqamaId == 0 || nameId == 0 || iconId == 0)
        {
            return;
        }

        var athan = entry is null ? "-" : SmallWidgetSupport.StripMeridiem(entry.AthanTime);
        var iqama = string.IsNullOrEmpty(entry?.IqamaTime)
            ? "-"
            : SmallWidgetSupport.StripMeridiem(entry.IqamaTime);
        views.SetTextViewText(nameId, StyledText(entry?.Name ?? string.Empty, isCurrent));
        views.SetTextViewText(athanId, StyledText(athan, isCurrent));
        views.SetTextViewText(iqamaId, StyledText(iqama, isCurrent));
        views.SetTextColor(nameId, isCurrent ? highlightColor : normalColor);
        views.SetTextColor(athanId, isCurrent ? highlightColor : secondaryColor);
        views.SetTextColor(iqamaId, isCurrent ? highlightColor : normalColor);
        views.SetInt(iconId, "setColorFilter", (isCurrent ? highlightColor : normalColor).ToArgb());
    }

    private static SpannableString StyledText(string text, bool isBold)
    {
        var styled = new SpannableString(text);
        if (isBold)
        {
            styled.SetSpan(
                new StyleSpan(global::Android.Graphics.TypefaceStyle.Bold),
                0,
                text.Length,
                SpanTypes.ExclusiveExclusive);
        }

        return styled;
    }
}
