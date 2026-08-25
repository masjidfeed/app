namespace MasjidApp.Core.PrayerTimes;

// FR-PT-07: result type carrying both athan and iqama times for all five daily prayers,
// Sunrise, and optional Jumuah entries.

/// <summary>
/// Immutable snapshot of all athan times, iqama times, and Jumuah sessions for a single calendar day.
/// </summary>
public sealed record DailyPrayerSchedule
{
    /// <summary>The Gregorian date this schedule applies to.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Calculated Fajr athan time (local masjid timezone).</summary>
    public TimeOnly FajrAthan { get; init; }

    /// <summary>Computed Fajr iqama time, or <see langword="null"/> when no rule is configured.</summary>
    public TimeOnly? FajrIqama { get; init; }

    /// <summary>Calculated Sunrise time (local masjid timezone).</summary>
    public TimeOnly Sunrise { get; init; }

    /// <summary>Calculated Dhuhr athan time (local masjid timezone).</summary>
    public TimeOnly DhuhrAthan { get; init; }

    /// <summary>Computed Dhuhr iqama time, or <see langword="null"/> when no rule is configured.</summary>
    public TimeOnly? DhuhrIqama { get; init; }

    /// <summary>Calculated Asr athan time (local masjid timezone).</summary>
    public TimeOnly AsrAthan { get; init; }

    /// <summary>Computed Asr iqama time, or <see langword="null"/> when no rule is configured.</summary>
    public TimeOnly? AsrIqama { get; init; }

    /// <summary>Calculated Maghrib athan time (local masjid timezone).</summary>
    public TimeOnly MaghribAthan { get; init; }

    /// <summary>Computed Maghrib iqama time, or <see langword="null"/> when no rule is configured.</summary>
    public TimeOnly? MaghribIqama { get; init; }

    /// <summary>Calculated Isha athan time (local masjid timezone).</summary>
    public TimeOnly IshaAthan { get; init; }

    /// <summary>Computed Isha iqama time, or <see langword="null"/> when no rule is configured.</summary>
    public TimeOnly? IshaIqama { get; init; }

    /// <summary>
    /// Configured Jumuah (Friday) prayer sessions, empty when the masjid publishes none.
    /// Carried on every day so the schedule can be shown ahead of Friday (FR-PT-03).
    /// </summary>
    public IReadOnlyList<JumuahTime> JumuahTimes { get; init; } = [];

    /// <summary>IANA timezone identifier used for all times in this schedule (FR-PT-06).</summary>
    public string Timezone { get; init; } = string.Empty;
}

/// <summary>A single Friday (Jumuah) prayer session entry.</summary>
/// <param name="Name">Display name of the Jumuah session, e.g. <c>"First Jumuah"</c>.</param>
/// <param name="Time">Start time of the khutbah/prayer in the masjid local timezone.</param>
/// <param name="Location">Optional venue name where this session is held.</param>
public sealed record JumuahTime(string Name, TimeOnly Time, string? Location);
