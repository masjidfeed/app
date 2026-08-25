using System.Text.Json;
using System.Text.Json.Serialization;

namespace MasjidApp.Core.SalahApi;

// API-01, API-02, API-03, API-07, NFR-RL-01: models for the SalahAPI 1.1 document.
// All fields are nullable to support tolerant/forward-compatible parsing.

/// <summary>
/// Top-level SalahAPI 1.1 document returned by the masjid's SalahAPI endpoint.
/// All properties are nullable so that missing or unknown fields never cause a
/// deserialization failure (NFR-RL-01).
/// </summary>
public sealed record SalahApiDocument
{
    /// <summary>
    /// Document format version, e.g. <c>"1.1"</c>.
    /// Corresponds to the <c>salahapi</c> JSON field.
    /// </summary>
    [JsonPropertyName("salahapi")]
    public string? Version { get; init; }

    /// <summary>Masjid identity, description, and last-updated timestamp.</summary>
    [JsonPropertyName("info")]
    public SalahApiInfo? Info { get; init; }

    /// <summary>Physical location including the IANA timezone (FR-PT-06).</summary>
    [JsonPropertyName("location")]
    public SalahApiLocation? Location { get; init; }

    /// <summary>Athan calculation parameters (method, angles, asr rule).</summary>
    [JsonPropertyName("calculationMethod")]
    public SalahApiCalculationMethod? CalculationMethod { get; init; }

    /// <summary>
    /// CSV-backed daily prayer times delivery configuration.
    /// Present when the masjid uses pre-computed times instead of on-device calculation.
    /// </summary>
    [JsonPropertyName("dailyPrayerTimes")]
    public SalahApiDailyPrayerTimes? DailyPrayerTimes { get; init; }
}

/// <summary>Masjid identity and document metadata.</summary>
public sealed record SalahApiInfo
{
    /// <summary>Human-readable document title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Optional description of the document or masjid.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Version of the prayer-times data.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>Optional provider contact.</summary>
    [JsonPropertyName("contact")]
    public SalahApiContact? Contact { get; init; }
}

/// <summary>Contact information for the SalahAPI document provider.</summary>
public sealed record SalahApiContact
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// Geographic location and timezone for the masjid.
/// <see cref="Timezone"/> is an IANA timezone identifier used for all time
/// computations, independent of the device timezone (FR-PT-06).
/// </summary>
public sealed record SalahApiLocation
{
    /// <summary>Latitude in decimal degrees.</summary>
    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    /// <summary>Longitude in decimal degrees.</summary>
    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }

    /// <summary>Elevation above sea level in metres.</summary>
    [JsonPropertyName("elevation")]
    public double? Elevation { get; init; }

    /// <summary>IANA timezone identifier, e.g. <c>"America/Los_Angeles"</c>.</summary>
    [JsonPropertyName("timezone")]
    public string? Timezone { get; init; }

    [JsonPropertyName("dateFormat")]
    public string? DateFormat { get; init; }

    [JsonPropertyName("timeFormat")]
    public string? TimeFormat { get; init; }
}

/// <summary>
/// Prayer-time calculation parameters following the SalahAPI 1.1 specification.
/// </summary>
public sealed record SalahApiCalculationMethod
{
    /// <summary>Named calculation method, e.g. <c>"ISNA"</c> or <c>"MWL"</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Asr juristic rule: <c>"Standard"</c> (Shafi/Hanbali/Maliki) or
    /// <c>"Hanafi"</c>.
    /// </summary>
    [JsonPropertyName("asrCalculationMethod")]
    public string? AsrCalculationMethod { get; init; }

    /// <summary>
    /// High-latitude correction method, e.g. <c>"AngleBased"</c>,
    /// <c>"MidNight"</c>, or <c>"OneSeventh"</c>.
    /// </summary>
    [JsonPropertyName("highLatitudeAdjustment")]
    public string? HighLatitudeAdjustment { get; init; }

    /// <summary>Per-prayer iqama calculation rules.</summary>
    [JsonPropertyName("iqamaCalculationRules")]
    public SalahApiIqamaCalculationRules? IqamaCalculationRules { get; init; }

    /// <summary>Friday Jumuah prayer schedule entries.</summary>
    [JsonPropertyName("jumuahRules")]
    public List<SalahApiJumuahRule>? JumuahRules { get; init; }
}

/// <summary>
/// Iqama time rules for each of the five daily prayers.
/// All prayers are nullable; absent entries mean "no rule configured".
/// </summary>
public sealed record SalahApiIqamaCalculationRules
{
    /// <summary>Day on which weekly iqama rules change.</summary>
    [JsonPropertyName("changeOn")]
    public string? ChangeOn { get; init; }

    /// <summary>Fajr iqama rule.</summary>
    [JsonPropertyName("fajr")]
    public SalahApiPrayerCalculationRule? Fajr { get; init; }

    /// <summary>Dhuhr iqama rule.</summary>
    [JsonPropertyName("dhuhr")]
    public SalahApiPrayerCalculationRule? Dhuhr { get; init; }

    /// <summary>Asr iqama rule.</summary>
    [JsonPropertyName("asr")]
    public SalahApiPrayerCalculationRule? Asr { get; init; }

    /// <summary>Maghrib iqama rule.</summary>
    [JsonPropertyName("maghrib")]
    public SalahApiPrayerCalculationRule? Maghrib { get; init; }

    /// <summary>Isha iqama rule.</summary>
    [JsonPropertyName("isha")]
    public SalahApiPrayerCalculationRule? Isha { get; init; }
}

/// <summary>
/// Prayer timing rule for an iqama or Jumuah session.
/// Exactly one of <see cref="Static"/> or <see cref="AfterAthanMinutes"/> should be
/// set; the others act as optional constraints.
/// </summary>
public sealed record SalahApiPrayerCalculationRule
{
    /// <summary>Frequency at which a calculated time changes.</summary>
    [JsonPropertyName("change")]
    public string? Change { get; init; }

    /// <summary>Minutes after athan to call iqama.</summary>
    [JsonPropertyName("afterAthanMinutes")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? AfterAthanMinutes { get; init; }

    /// <summary>
    /// Fixed clock time, e.g. <c>"13:30"</c>.
    /// Takes precedence over <see cref="AfterAthanMinutes"/> when set.
    /// </summary>
    [JsonPropertyName("static")]
    public string? Static { get; init; }

    /// <summary>
    /// Round the computed iqama time up to the nearest multiple of this many minutes.
    /// </summary>
    [JsonPropertyName("roundMinutes")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? RoundMinutes { get; init; }

    /// <summary>Earliest allowed iqama time, e.g. <c>"20:00"</c>.</summary>
    [JsonPropertyName("earliest")]
    public string? Earliest { get; init; }

    /// <summary>Latest allowed iqama time, e.g. <c>"22:30"</c>.</summary>
    [JsonPropertyName("latest")]
    public string? Latest { get; init; }

    /// <summary>Minutes before the prayer window ends.</summary>
    [JsonPropertyName("beforeEndMinutes")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? BeforeEndMinutes { get; init; }

    /// <summary>Conditional replacements for this rule.</summary>
    [JsonPropertyName("overrides")]
    public List<SalahApiPrayerCalculationOverride>? Overrides { get; init; }
}

/// <summary>Conditional replacement for a prayer calculation rule.</summary>
public sealed record SalahApiPrayerCalculationOverride
{
    [JsonPropertyName("condition")]
    public string? Condition { get; init; }

    [JsonPropertyName("time")]
    public SalahApiPrayerCalculationRule? Time { get; init; }
}

/// <summary>A single Friday (Jumuah) prayer session.</summary>
public sealed record SalahApiJumuahRule
{
    /// <summary>Display name, e.g. <c>"First Jumuah"</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Calculation rule for the Jumuah start time.</summary>
    [JsonPropertyName("time")]
    public SalahApiPrayerCalculationRule? Time { get; init; }

    /// <summary>Physical location where this Jumuah is held.</summary>
    [JsonPropertyName("location")]
    public SalahApiJumuahLocation? Location { get; init; }
}

/// <summary>Location details for a Jumuah prayer session.</summary>
public sealed record SalahApiJumuahLocation
{
    /// <summary>Name of the prayer venue, e.g. <c>"Main Prayer Hall"</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Street address of the venue.</summary>
    [JsonPropertyName("address")]
    public string? Address { get; init; }
}

/// <summary>
/// Configuration for the CSV-backed daily prayer times delivery mode.
/// When <see cref="CsvUrl"/> is non-null the app downloads pre-computed times
/// instead of running on-device calculation (batch A3).
/// </summary>
public sealed record SalahApiDailyPrayerTimes
{
    /// <summary>
    /// URL template for the CSV file containing pre-computed daily prayer times.
    /// </summary>
    [JsonPropertyName("csvUrl")]
    public string? CsvUrl { get; init; }

    /// <summary>
    /// Arbitrary query parameter bag for the CSV URL request.
    /// Stored as a raw <see cref="JsonElement"/> to allow tolerant forward-compatible
    /// parsing of any additional parameters the server may add.
    /// </summary>
    [JsonPropertyName("csvUrlParameters")]
    public JsonElement? CsvUrlParameters { get; init; }

    [JsonPropertyName("dateFormat")]
    public string? DateFormat { get; init; }

    [JsonPropertyName("timeFormat")]
    public string? TimeFormat { get; init; }
}
