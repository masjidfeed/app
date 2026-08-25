using System.Text.Json.Serialization;

namespace MasjidApp.Core.Widgets;

// TC-04, FR-WG-01, FR-WG-02: pre-resolved snapshot written to the shared widget
// container so the native widget process does zero domain computation.

/// <summary>
/// Render-ready prayer data written to the shared widget container (iOS App Group /
/// Android SharedPreferences).  All times are pre-formatted strings so the native
/// widget process does not need to perform any calculations.
/// </summary>
/// <remarks>
/// The snapshot carries a rolling window of <see cref="Days"/> (seven days starting
/// "today" in the masjid timezone) so the widget remains self-sufficient and keeps
/// rolling the displayed day over without an app refresh. The whole snapshot - and in
/// particular the fixed-size <see cref="Days"/> list - is overwritten wholesale on every
/// refresh (the shared file is replaced atomically), so nothing accumulates over time.
/// </remarks>
public sealed record WidgetData
{
    /// <summary>Display name of the active masjid.</summary>
    [JsonPropertyName("masjidName")]
    public string MasjidName { get; init; } = string.Empty;

    /// <summary>IANA timezone identifier for the masjid (FR-PT-06).</summary>
    [JsonPropertyName("timezone")]
    public string Timezone { get; init; } = string.Empty;

    /// <summary>
    /// Unix epoch seconds at which this snapshot was generated. Used by the widget to
    /// detect and surface stale data (FR-WG-01). Zero when unknown.
    /// </summary>
    [JsonPropertyName("generatedAtEpochSeconds")]
    public long GeneratedAtEpochSeconds { get; init; }

    /// <summary>
    /// Rolling window of daily prayer schedules (typically seven days starting today in
    /// the masjid timezone). The widget selects the day containing the current instant,
    /// rolls the date over at each day boundary, and shows a staleness message once the
    /// current instant runs past the covered window. Replaced wholesale on every refresh.
    /// </summary>
    [JsonPropertyName("days")]
    public IReadOnlyList<WidgetDayData> Days
    {
        get => _days;
        init => _days = value ?? [];
    }

    private readonly IReadOnlyList<WidgetDayData> _days = [];
}

/// <summary>A single day's worth of pre-formatted prayer data inside <see cref="WidgetData"/>.</summary>
public sealed record WidgetDayData
{
    /// <summary>
    /// Unix epoch seconds for the start of this day (midnight in the masjid timezone).
    /// The widget uses this to pick the current day and to roll the displayed date over
    /// at each day boundary without an app refresh (FR-WG-06). Zero when unknown.
    /// </summary>
    [JsonPropertyName("dateEpochSeconds")]
    public long DateEpochSeconds { get; init; }

    /// <summary>Hijri date formatted for display, e.g. "15 Dhul-Hijjah 1446".</summary>
    [JsonPropertyName("hijriDate")]
    public string HijriDate { get; init; } = string.Empty;

    /// <summary>Gregorian date formatted for display, e.g. "Friday, 12 Jun 2026".</summary>
    [JsonPropertyName("gregorianDate")]
    public string GregorianDate { get; init; } = string.Empty;

    /// <summary>
    /// This day's prayers (Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha) in display order,
    /// each with pre-formatted athan and iqama strings (FR-WG-01, FR-WG-02).
    /// </summary>
    [JsonPropertyName("prayers")]
    public IReadOnlyList<WidgetPrayerEntry> Prayers
    {
        get => _prayers;
        init => _prayers = value ?? [];
    }

    private readonly IReadOnlyList<WidgetPrayerEntry> _prayers = [];
}

/// <summary>A single prayer entry inside <see cref="WidgetDayData"/>.</summary>
/// <param name="Name">Localised prayer name, e.g. <c>"Fajr"</c>.</param>
/// <param name="AthanTime">Pre-formatted athan time, e.g. <c>"4:30 AM"</c>.</param>
/// <param name="IqamaTime">Pre-formatted iqama time, or empty when not configured.</param>
/// <param name="AthanEpochSeconds">Unix epoch seconds for the athan, so the widget can
/// advance the countdown/progress to the following prayer without an app refresh. Zero when unknown.</param>
/// <param name="IqamaEpochSeconds">Unix epoch seconds for the iqama, or zero when not configured.</param>
public sealed record WidgetPrayerEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("athanTime")] string AthanTime,
    [property: JsonPropertyName("iqamaTime")] string IqamaTime,
    [property: JsonPropertyName("athanEpochSeconds")] long AthanEpochSeconds = 0,
    [property: JsonPropertyName("iqamaEpochSeconds")] long IqamaEpochSeconds = 0);
