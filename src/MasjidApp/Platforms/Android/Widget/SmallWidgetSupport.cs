using System.Text.Json;
using Android.Content;
using MasjidApp.Core.Widgets;

namespace MasjidApp.Platforms.Android.Widget;

internal static class SmallWidgetSupport
{
    internal static WidgetData? LoadWidgetData(Context context)
    {
        var prefs = context.GetSharedPreferences("widget_data", FileCreationMode.Private);
        var json = prefs?.GetString("widget_data_json", null);
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, WidgetDataJsonContext.Default.WidgetData);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static WidgetCountdownTarget? FindNextTarget(WidgetData data, long nowEpochSeconds) =>
        WidgetDaySelector.NextCountdownTarget(data, nowEpochSeconds);

    internal static WidgetPrayerEntry? GetPrayer(WidgetDayData? day, string name)
    {
        if (day is null)
        {
            return null;
        }

        foreach (var prayer in day.Prayers)
        {
            if (string.Equals(prayer.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return prayer;
            }
        }

        return null;
    }

    internal static string DisplayTime(WidgetPrayerEntry? prayer)
    {
        if (prayer is null)
        {
            return "-";
        }

        var time = string.IsNullOrEmpty(prayer.IqamaTime) ? prayer.AthanTime : prayer.IqamaTime;
        return StripMeridiem(time);
    }

    internal static string DisplayTime(WidgetCountdownTarget target) =>
        StripMeridiem(target.Kind == WidgetTransitionKind.Athan
            ? target.Prayer.AthanTime
            : target.Prayer.IqamaTime);

    internal static string CompactCountdownLabel(WidgetCountdownTarget target) =>
        IsSunrise(target) ? "until" : $"till {target.Kind}";

    internal static string CountdownLabel(WidgetCountdownTarget target) =>
        IsSunrise(target) ? "until Sunrise" : $"till {target.Prayer.Name} {target.Kind}";

    internal static int RemainingProgress(
        WidgetCountdownTarget target,
        WidgetData data,
        long nowEpochSeconds) =>
        (int)Math.Round(WidgetDaySelector.RemainingProgress(data, target, nowEpochSeconds) * 1000d);

    private static bool IsSunrise(WidgetCountdownTarget target) =>
        string.Equals(target.Prayer.Name, "Sunrise", StringComparison.OrdinalIgnoreCase);

    internal static string StripMeridiem(string time)
    {
        if (time.EndsWith(" AM", StringComparison.OrdinalIgnoreCase) ||
            time.EndsWith(" PM", StringComparison.OrdinalIgnoreCase))
        {
            return time[..^3];
        }

        return time;
    }

    internal static string CompactHijriDate(string date)
    {
        var parts = date.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return date.ToUpperInvariant();
        }

        var month = string.Join(' ', parts[1..^1]);
        var shortenedMonth = month switch
        {
            "Muharram" => "MUHR",
            "Safar" => "SAFR",
            "Rabi' al-Awwal" => "RAB1",
            "Rabi' al-Thani" => "RAB2",
            "Jumada al-Awwal" => "JUM1",
            "Jumada al-Thani" => "JUM2",
            "Rajab" => "RAJB",
            "Sha'ban" => "SHBN",
            "Ramadan" => "RAMD",
            "Shawwal" => "SHWL",
            "Dhul-Qa'dah" => "DHQD",
            "Dhul-Hijjah" => "DHHJ",
            _ => month[..Math.Min(4, month.Length)].ToUpperInvariant(),
        };
        return $"{shortenedMonth} {parts[0]}";
    }

    internal static string CompactGregorianDate(string date)
    {
        var dateParts = date.Split(',', 2, StringSplitOptions.TrimEntries);
        var dateValue = dateParts.Length > 1 ? dateParts[1] : dateParts[0];
        var parts = dateValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return date.ToUpperInvariant();
        }

        var month = parts[1][..Math.Min(3, parts[1].Length)].ToUpperInvariant();
        return $"{month} {parts[0]}";
    }

    internal static int GetResourceId(string name)
    {
        var field = typeof(Resource.Id).GetField(name);
        return field is null ? 0 : (int)(field.GetValue(null) ?? 0);
    }

    internal static int GetBackgroundResource(string? prayerName)
    {
        if (prayerName?.Contains("Fajr", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Resource.Drawable.widget_background_fajr;
        }

        if (prayerName?.Contains("Sunrise", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Resource.Drawable.widget_background_sunrise;
        }

        if (prayerName?.Contains("Dhuhr", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Resource.Drawable.widget_background_dhuhr;
        }

        if (prayerName?.Contains("Asr", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Resource.Drawable.widget_background_asr;
        }

        if (prayerName?.Contains("Maghrib", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Resource.Drawable.widget_background_maghrib;
        }

        return Resource.Drawable.widget_background_isha;
    }
}
