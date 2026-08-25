namespace MasjidApp.Core.PrayerTimes;

/// <summary>Formats a compact minute-level summary for an upcoming prayer event.</summary>
public static class PrayerEventCountdownFormatter
{
    /// <summary>Formats text such as <c>2 hr 10 min till Asr Athan</c>.</summary>
    public static string Format(UpcomingPrayerEvent target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var totalMinutes = Math.Max(0, (int)target.TimeRemaining.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        var duration = hours > 0
            ? $"{hours} hr {minutes} min"
            : $"{minutes} min";
        return $"{duration} till {target.Name} {target.Kind}";
    }
}