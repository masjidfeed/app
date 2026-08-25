using System.Globalization;
using MasjidApp.Core.Observability;
using MasjidApp.Core.SalahApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TimeZoneConverter;
using SalahApiCalc = SalahAPI.Calculations.PrayerTimes;

namespace MasjidApp.Core.PrayerTimes;

// FR-PT-06, FR-PT-07, FR-PT-08, NFR-PF-02, QA-02, QA-06

/// <summary>
/// On-device prayer-time and iqama calculator backed by the <c>SalahAPI</c> NuGet package.
/// Applies the masjid IANA timezone independently of the device timezone (FR-PT-06),
/// and resolves iqama rules with the priority: Ramadan override &gt; DST override &gt; base rule
/// (FR-PT-08).
/// </summary>
public sealed partial class PrayerTimeCalculator : IPrayerTimeCalculator
{
    private readonly ILogger<PrayerTimeCalculator> _logger;

    /// <summary>
    /// Initialises the calculator with an optional logger. When no logger is supplied a
    /// no-op logger is used so the type stays trivially constructable in unit tests.
    /// </summary>
    /// <param name="logger">Logger for handled calculation failures (NFR-RL-01).</param>
    public PrayerTimeCalculator(ILogger<PrayerTimeCalculator>? logger = null)
    {
        _logger = logger ?? NullLogger<PrayerTimeCalculator>.Instance;
    }

    /// <inheritdoc />
    public DailyPrayerSchedule Calculate(SalahApiDocument document, DateOnly targetDate)
    {
        var date = targetDate;
        var timezone = document.Location?.Timezone ?? "UTC";
        var lat = document.Location?.Latitude ?? 0.0;
        var lon = document.Location?.Longitude ?? 0.0;
        var elevation = document.Location?.Elevation ?? 0.0;
        var method = document.CalculationMethod?.Name ?? "ISNA";
        var school = MapSchool(document.CalculationMethod?.AsrCalculationMethod);
        var highLatAdj = MapHighLatAdj(document.CalculationMethod?.HighLatitudeAdjustment);

        var pt = new SalahApiCalc(method, school);
        var dateTime = date.ToDateTime(TimeOnly.MinValue);

        Dictionary<string, string> times;
        try
        {
            times = pt.GetTimes(
                date: dateTime,
                latitude: lat,
                longitude: lon,
                elevation: elevation,
                latitudeAdjustmentMethod: highLatAdj ?? SalahApiCalc.LatitudeAdjustmentMethodNone,
                midnightMode: null,
                format: SalahApiCalc.TimeFormat24H,
                ianaTimezone: timezone);
        }
#pragma warning disable CA1031 // NFR-RL-01: degrade gracefully on third-party library failures; the exception is logged and metered below.
        catch (Exception ex)
        {
            LogCalculationFailed(_logger, method, ex);
            MasjidTelemetry.DegradedOperations.Add(1, MasjidTelemetry.OperationTag("prayer_time_calculation"));
            times = [];
        }
#pragma warning restore CA1031

        var fajrAthan = ParseAthan(times, SalahApiCalc.Fajr);
        var sunrise = ParseAthan(times, SalahApiCalc.Sunrise);
        var dhuhrAthan = ParseAthan(times, SalahApiCalc.Zhuhr);
        var asrAthan = ParseAthan(times, SalahApiCalc.Asr);
        var maghribAthan = ParseAthan(times, SalahApiCalc.Maghrib);
        var ishaAthan = ParseAthan(times, SalahApiCalc.Isha);

        // Resolve iqama rules with override priority: Ramadan > DST > base (FR-PT-08).
        var isRamadan = SalahAPI.Calculations.HijriDateConverter.IsRamadan(dateTime);
        var isDst = GetIsDst(timezone, dateTime);
        var baseRules = document.CalculationMethod?.IqamaCalculationRules;
        var fajrRule = PickRule(baseRules?.Fajr, isRamadan, isDst);
        var dhuhrRule = PickRule(baseRules?.Dhuhr, isRamadan, isDst);
        var asrRule = PickRule(baseRules?.Asr, isRamadan, isDst);
        var maghribRule = PickRule(baseRules?.Maghrib, isRamadan, isDst);
        var ishaRule = PickRule(baseRules?.Isha, isRamadan, isDst);

        var jumuahTimes = BuildJumuahTimes(document);

        return new DailyPrayerSchedule
        {
            Date = date,
            FajrAthan = fajrAthan,
            FajrIqama = ComputeIqama(fajrAthan, sunrise, fajrRule),
            Sunrise = sunrise,
            DhuhrAthan = dhuhrAthan,
            DhuhrIqama = ComputeIqama(dhuhrAthan, asrAthan, dhuhrRule),
            AsrAthan = asrAthan,
            AsrIqama = ComputeIqama(asrAthan, maghribAthan, asrRule),
            MaghribAthan = maghribAthan,
            MaghribIqama = ComputeIqama(maghribAthan, ishaAthan, maghribRule),
            IshaAthan = ishaAthan,
            IshaIqama = ComputeIqama(ishaAthan, null, ishaRule),
            JumuahTimes = jumuahTimes,
            Timezone = timezone,
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses an athan time from the dictionary returned by <c>PrayerTimes.GetTimes</c>.
    /// Returns <see cref="TimeOnly.MinValue"/> for invalid ("-----") or missing entries
    /// (NFR-RL-01).
    /// </summary>
    private static TimeOnly ParseAthan(Dictionary<string, string> times, string key)
    {
        if (times.TryGetValue(key, out var val) &&
            val != SalahApiCalc.InvalidTime &&
            TimeOnly.TryParseExact(val, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            return t;
        }

        return TimeOnly.MinValue;
    }

    /// <summary>
    /// Computes the iqama time for a single prayer given the athan time and iqama rule.
    /// Returns <see langword="null"/> when no rule is configured.
    /// </summary>
    private static TimeOnly? ComputeIqama(
        TimeOnly athan,
        TimeOnly? prayerEnd,
        SalahApiPrayerCalculationRule? rule)
    {
        if (rule is null)
        {
            return null;
        }

        // Static fixed time takes precedence over offset-based rules.
        if (rule.Static is not null)
        {
            if (TimeOnly.TryParseExact(rule.Static, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var staticTime))
            {
                return staticTime;
            }

            return null;
        }

        TimeOnly? rawIqama = null;
        if (rule.AfterAthanMinutes is not null)
        {
            rawIqama = athan.AddMinutes(rule.AfterAthanMinutes.Value);
        }
        else if (rule.BeforeEndMinutes is not null && prayerEnd is not null)
        {
            rawIqama = prayerEnd.Value.AddMinutes(-rule.BeforeEndMinutes.Value);
        }

        if (rawIqama is not null)
        {
            if (rule.RoundMinutes is { } roundBy && roundBy > 0)
            {
                rawIqama = RoundUpToNextMultiple(rawIqama.Value, roundBy);
            }

            if (rule.Earliest is not null &&
                TimeOnly.TryParseExact(rule.Earliest, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var earliest) &&
                rawIqama.Value < earliest)
            {
                rawIqama = earliest;
            }

            if (rule.Latest is not null &&
                TimeOnly.TryParseExact(rule.Latest, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var latest) &&
                rawIqama.Value > latest)
            {
                rawIqama = latest;
            }

            return rawIqama;
        }

        return null;
    }

    /// <summary>
    /// Rounds <paramref name="time"/> up to the next multiple of <paramref name="minutes"/>
    /// minutes. If <paramref name="time"/> is already on a boundary it is returned unchanged.
    /// </summary>
    private static TimeOnly RoundUpToNextMultiple(TimeOnly time, int minutes)
    {
        var totalMinutes = time.Hour * 60 + time.Minute;
        var remainder = totalMinutes % minutes;
        if (remainder == 0)
        {
            return time;
        }

        var rounded = totalMinutes + (minutes - remainder);
        return new TimeOnly(rounded / 60 % 24, rounded % 60);
    }

    /// <summary>
    /// Selects the effective iqama rule according to the override priority:
    /// Ramadan override &gt; DST override &gt; base rule.
    /// </summary>
    private static SalahApiPrayerCalculationRule? PickRule(
        SalahApiPrayerCalculationRule? baseRule,
        bool isRamadan,
        bool isDst)
    {
        if (baseRule is null)
        {
            return null;
        }

        if (isRamadan && FindOverride(baseRule, "ramadan") is { } ramadan)
        {
            return ramadan;
        }

        return isDst && FindOverride(baseRule, "daylightSavingsTime") is { } dst
            ? dst
            : baseRule;
    }

    private static SalahApiPrayerCalculationRule? FindOverride(
        SalahApiPrayerCalculationRule rule,
        string condition) =>
        rule.Overrides?
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Condition, condition, StringComparison.OrdinalIgnoreCase))
            ?.Time;

    /// <summary>
    /// Returns whether <paramref name="date"/> falls in Daylight Saving Time for
    /// <paramref name="ianaTimezone"/>.  Returns <see langword="false"/> on any
    /// lookup failure (NFR-RL-01).
    /// </summary>
    private bool GetIsDst(string ianaTimezone, DateTime date)
    {
        try
        {
            var tz = TZConvert.GetTimeZoneInfo(ianaTimezone);
            return tz.IsDaylightSavingTime(date);
        }
#pragma warning disable CA1031 // NFR-RL-01: degrade gracefully on timezone lookup failure; the exception is logged and metered below.
        catch (Exception ex)
        {
            LogDstLookupFailed(_logger, ianaTimezone, ex);
            MasjidTelemetry.DegradedOperations.Add(1, MasjidTelemetry.OperationTag("dst_lookup"));
            return false;
        }
#pragma warning restore CA1031
    }

    // ── Source-generated high-performance log messages (CA1848) ─────────────────

    [LoggerMessage(Level = LogLevel.Warning, Message = "Prayer-time calculation failed for method '{Method}'; returning an empty schedule.")]
    private static partial void LogCalculationFailed(ILogger logger, string method, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "DST lookup failed for timezone '{Timezone}'; assuming standard time.")]
    private static partial void LogDstLookupFailed(ILogger logger, string timezone, Exception ex);

    /// <summary>
    /// Builds the configured Jumuah sessions (FR-PT-03). Sessions are reported on every
    /// day so the schedule can be surfaced ahead of Friday; callers decide when to show them.
    /// </summary>
    private static List<JumuahTime> BuildJumuahTimes(SalahApiDocument document)
    {
        var rules = document.CalculationMethod?.JumuahRules;
        if (rules is null)
        {
            return [];
        }

        var result = new List<JumuahTime>(rules.Count);
        foreach (var rule in rules)
        {
            if (rule.Time?.Static is not { } staticTime ||
                !TimeOnly.TryParseExact(staticTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var jTime))
            {
                continue;
            }

            result.Add(new JumuahTime(
                Name: rule.Name ?? string.Empty,
                Time: jTime,
                Location: rule.Location?.Name));
        }

        return result;
    }

    /// <summary>Maps the SalahAPI <c>asrCalculationMethod</c> string to the SalahAPI package school constant.</summary>
    private static string MapSchool(string? asrMethod) =>
        string.Equals(asrMethod, "Hanafi", StringComparison.OrdinalIgnoreCase)
            ? SalahApiCalc.SchoolHanafi
            : SalahApiCalc.SchoolStandard;

    /// <summary>
    /// Maps the SalahAPI <c>highLatitudeAdjustment</c> string to the SalahAPI package
    /// latitude-adjustment-method constant, returning <see langword="null"/> for unrecognised values.
    /// </summary>
    private static string? MapHighLatAdj(string? adj) => adj switch
    {
        "AngleBased" => SalahApiCalc.LatitudeAdjustmentMethodAngle,
        "MiddleOfTheNight" or "MidNight" => SalahApiCalc.LatitudeAdjustmentMethodMotn,
        "OneSeventh" => SalahApiCalc.LatitudeAdjustmentMethodOneSeventh,
        _ => null,
    };
}
