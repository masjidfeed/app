using MasjidApp.Core.Http;
using MasjidApp.Core.SalahApi;
using MasjidApp.Core.Services;

namespace MasjidApp.Core.PrayerTimes;

// FR-PT-01, FR-PT-02, FR-PT-03, FR-PT-04, FR-PT-09, NFR-OF-01, NFR-RL-01

/// <summary>
/// Default implementation of <see cref="IPrayerTimesProvider"/>.
/// Fetches the SalahAPI document, calculates athan/iqama times, and merges
/// CSV overrides according to the API-04 merge policy.
/// Never throws - failures are encoded in the returned <see cref="ApiResult{T}"/> (NFR-RL-01).
/// </summary>
public sealed class PrayerTimesProvider : IPrayerTimesProvider
{
    /// <summary>Forward days whose CSV coverage determines today's reported freshness.</summary>
    private const int ForwardWindowDays = 30;

    private readonly IMasjidContext _context;
    private readonly ISalahApiService _salahApiService;
    private readonly IPrayerTimeCalculator _calculator;
    private readonly ICsvPrayerTimesService _csvService;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Constructs the provider with all required dependencies.
    /// </summary>
    public PrayerTimesProvider(
        IMasjidContext context,
        ISalahApiService salahApiService,
        IPrayerTimeCalculator calculator,
        ICsvPrayerTimesService csvService,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(salahApiService);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(csvService);

        _context = context;
        _salahApiService = salahApiService;
        _calculator = calculator;
        _csvService = csvService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<ApiResult<DailyPrayerSchedule>> GetTodayAsync(
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        var docResult = await FetchDocumentAsync(cachePolicy, ct).ConfigureAwait(false);
        if (docResult is null)
        {
            return new ApiResult<DailyPrayerSchedule>.Failed(null, "No SalahAPI URL configured.");
        }

        if (docResult.ValueOrDefault is not SalahApiDocument document)
        {
            return new ApiResult<DailyPrayerSchedule>.Failed(
                (docResult as ApiResult<SalahApiDocument>.Failed)?.Cause,
                "SalahAPI document unavailable.");
        }

        var today = CurrentDate(document);
        var calculated = _calculator.Calculate(document, today);

        // Coverage is evaluated across the window the UI actually renders (today plus the
        // forward schedule), not just today, so a gap later in the window is still reported.
        var csv = await _csvService
            .GetCsvTimesAsync(document, today, today.AddDays(ForwardWindowDays - 1), cachePolicy, ct)
            .ConfigureAwait(false);
        var csvRow = csv.Rows.FirstOrDefault(r => r.Date == today);
        var merged = PrayerTimesMerger.Merge(calculated, csvRow);

        var result = docResult switch
        {
            ApiResult<SalahApiDocument>.Fresh f
                => new ApiResult<DailyPrayerSchedule>.Fresh(merged, f.FetchedAt),
            ApiResult<SalahApiDocument>.Cached c
                => new ApiResult<DailyPrayerSchedule>.Cached(merged, c.FetchedAt),
            ApiResult<SalahApiDocument>.NotModified nm
                => new ApiResult<DailyPrayerSchedule>.NotModified(merged, nm.FetchedAt),
            ApiResult<SalahApiDocument>.Stale s
                => new ApiResult<DailyPrayerSchedule>.Stale(merged, s.FetchedAt, s.Cause),
            _ => (ApiResult<DailyPrayerSchedule>)new ApiResult<DailyPrayerSchedule>.Failed(null, "Unexpected result case."),
        };

        // FR-PT-04: when a CSV feed is configured but cannot cover the window, the times on
        // screen are calculated estimates rather than the masjid's published schedule.
        // Report that as degraded instead of letting the fallback pass for fresh data.
        return csv.IsDegraded && result.FetchedAtOrNull is { } fetchedAt
            ? new ApiResult<DailyPrayerSchedule>.Stale(merged, csv.LastUpdatedAt ?? fetchedAt, null)
            : result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DailyPrayerSchedule>> GetCurrentRangeAsync(
        int dayCount,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dayCount, 1);

        var docResult = await FetchDocumentAsync(cachePolicy, ct).ConfigureAwait(false);
        if (docResult?.ValueOrDefault is not SalahApiDocument document)
        {
            return [];
        }

        var from = CurrentDate(document);
        return await BuildRangeAsync(document, from, from.AddDays(dayCount - 1), cachePolicy, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DailyPrayerSchedule>> GetRangeAsync(
        DateOnly from,
        DateOnly toDate,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        var docResult = await FetchDocumentAsync(cachePolicy, ct).ConfigureAwait(false);
        if (docResult?.ValueOrDefault is not SalahApiDocument document)
        {
            return [];
        }

        return await BuildRangeAsync(document, from, toDate, cachePolicy, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DailyPrayerSchedule>> BuildRangeAsync(
        SalahApiDocument document,
        DateOnly from,
        DateOnly toDate,
        ApiCachePolicy cachePolicy,
        CancellationToken ct)
    {
        // Read CSV rows for the entire range in one call; never throws (NFR-RL-01).
        var csv = await _csvService
            .GetCsvTimesAsync(document, from, toDate, cachePolicy, ct)
            .ConfigureAwait(false);
        var csvByDate = csv.Rows.ToDictionary(r => r.Date);

        var schedules = new List<DailyPrayerSchedule>();
        for (var date = from; date <= toDate; date = date.AddDays(1))
        {
            var calculated = _calculator.Calculate(document, date);
            csvByDate.TryGetValue(date, out var csvRow);
            schedules.Add(PrayerTimesMerger.Merge(calculated, csvRow));
        }

        return schedules;
    }

    private DateOnly CurrentDate(SalahApiDocument document) =>
        MasjidTimeZone.Today(document.Location?.Timezone, _timeProvider);

    /// <summary>
    /// Resolves the SalahAPI URL from the active masjid config and fetches the document.
    /// Returns <see langword="null"/> when no masjid is configured or the URL is absent.
    /// </summary>
    private async Task<ApiResult<SalahApiDocument>?> FetchDocumentAsync(
        ApiCachePolicy cachePolicy,
        CancellationToken ct)
    {
        var configResult = await _context.GetConfigAsync(cachePolicy, ct).ConfigureAwait(false);
        var salahApiUrl = configResult?.ValueOrDefault?.Endpoints?.SalahApiUrl;
        if (string.IsNullOrWhiteSpace(salahApiUrl))
        {
            return null;
        }

        return await _salahApiService
            .GetDocumentAsync(salahApiUrl, cachePolicy, ct)
            .ConfigureAwait(false);
    }
}
