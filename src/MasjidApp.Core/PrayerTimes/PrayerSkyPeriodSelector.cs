namespace MasjidApp.Core.PrayerTimes;

/// <summary>Selects the sky gradient period shared by app countdown cards and native widgets.</summary>
internal static class PrayerSkyPeriodSelector
{
    private const long SunriseAndMaghribWindowSeconds = 15 * 60L;

    public static string? Select(
        long nowEpochSeconds,
        long fajrEpochSeconds,
        long sunriseEpochSeconds,
        long asrEpochSeconds,
        long maghribEpochSeconds)
    {
        if (fajrEpochSeconds <= 0 ||
            fajrEpochSeconds >= sunriseEpochSeconds ||
            sunriseEpochSeconds >= asrEpochSeconds ||
            asrEpochSeconds >= maghribEpochSeconds)
        {
            return null;
        }

        if (nowEpochSeconds >= sunriseEpochSeconds - SunriseAndMaghribWindowSeconds &&
            nowEpochSeconds < sunriseEpochSeconds + SunriseAndMaghribWindowSeconds)
        {
            return "Sunrise";
        }

        if (nowEpochSeconds >= maghribEpochSeconds - SunriseAndMaghribWindowSeconds &&
            nowEpochSeconds < maghribEpochSeconds + SunriseAndMaghribWindowSeconds)
        {
            return "Maghrib";
        }

        if (nowEpochSeconds >= fajrEpochSeconds && nowEpochSeconds < sunriseEpochSeconds)
        {
            return "Fajr";
        }

        if (nowEpochSeconds >= sunriseEpochSeconds && nowEpochSeconds < asrEpochSeconds)
        {
            return "Dhuhr";
        }

        if (nowEpochSeconds >= asrEpochSeconds && nowEpochSeconds < maghribEpochSeconds)
        {
            return "Asr";
        }

        return "Isha";
    }
}
