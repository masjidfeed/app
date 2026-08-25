using System.Net;
using System.Text;
using System.Text.Json;
using MasjidApp.Core.Http;
using MasjidApp.Core.Observability;
using MasjidApp.Core.SalahApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MasjidApp.Core.PrayerTimes;

// API-03, FR-PT-04, FR-PT-09, NFR-OF-01, NFR-OF-03, NFR-RL-01

/// <summary>
/// Default implementation of <see cref="ICsvPrayerTimesService"/>.
/// Reads every request from <see cref="ICsvPrayerTimesStore"/> so the schedule renders
/// offline, and refreshes that store from the network only when the feed is due for its
/// daily change check or local coverage drops below the forward window.
/// </summary>
/// <remarks>
/// Refresh policy:
/// <list type="bullet">
///   <item><description>
///     A refresh downloads <see cref="FetchWindowMonths"/> months starting today and
///     replaces the stored rows in one transaction, only after the download and parse
///     have both succeeded.
///   </description></item>
///   <item><description>
///     Requests carry <c>If-None-Match</c>, so an unchanged feed costs a single
///     <c>304 Not Modified</c> with no body.
///   </description></item>
///   <item><description>
///     Fewer than <see cref="ForwardCoverageDays"/> stored days ahead of today always
///     forces a refresh attempt, ignoring the daily throttle.
///   </description></item>
/// </list>
/// Inject a named <see cref="HttpClient"/> (e.g. <c>"csv"</c>) via the MAUI service
/// container so the caller controls base address, TLS, and timeouts.
/// </remarks>
public sealed partial class CsvPrayerTimesService : ICsvPrayerTimesService
{
    /// <summary>Forward days that must remain stored before the daily throttle applies.</summary>
    public const int ForwardCoverageDays = 30;

    /// <summary>Months of data requested on every refresh.</summary>
    public const int FetchWindowMonths = 6;

    /// <summary>Minimum interval between change checks of an adequately covered feed.</summary>
    public static readonly TimeSpan FreshnessCheckInterval = TimeSpan.FromHours(24);

    private readonly HttpClient _http;
    private readonly ICsvPrayerTimesStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CsvPrayerTimesService> _logger;
    private readonly object _backgroundRefreshLock = new();
    private readonly HashSet<string> _automaticallyRefreshedUrls = [];

    /// <summary>
    /// Initialises the service with the transport and durable store it coordinates.
    /// </summary>
    /// <param name="httpClient">HTTP client for raw (non-JSON) CSV downloads.</param>
    /// <param name="store">Durable per-day store for downloaded rows.</param>
    /// <param name="timeProvider">Clock driving the daily check cadence; defaults to the system clock.</param>
    /// <param name="logger">Logger for handled download/parse failures (NFR-RL-01).</param>
    public CsvPrayerTimesService(
        HttpClient httpClient,
        ICsvPrayerTimesStore store,
        TimeProvider? timeProvider = null,
        ILogger<CsvPrayerTimesService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(store);

        _http = httpClient;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CsvPrayerTimesService>.Instance;
    }

    /// <inheritdoc/>
    public async Task<CsvPrayerTimesResult> GetCsvTimesAsync(
        SalahApiDocument document,
        DateOnly from,
        DateOnly toDate,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ct.ThrowIfCancellationRequested();

        var csvUrl = document.DailyPrayerTimes?.CsvUrl;
        if (string.IsNullOrWhiteSpace(csvUrl))
        {
            return CsvPrayerTimesResult.NotConfigured;
        }

        var state = await _store.GetStateAsync(ct).ConfigureAwait(false);

        // Rows downloaded from another masjid's feed must never be served (API-06, API-10).
        if (!string.Equals(state.FeedUrl, csvUrl, StringComparison.Ordinal))
        {
            await _store.ResetAsync(csvUrl, ct).ConfigureAwait(false);
            state = CsvPrayerTimesFeedState.Empty with { FeedUrl = csvUrl };
        }

        var today = MasjidTimeZone.Today(document.Location?.Timezone, _timeProvider);
        if (ShouldRefresh(state, today, cachePolicy))
        {
            if (cachePolicy == ApiCachePolicy.ForceRefresh)
            {
                await TryRefreshAsync(document, csvUrl, today, state.ETag, ct).ConfigureAwait(false);
                state = await _store.GetStateAsync(ct).ConfigureAwait(false);
            }
            else
            {
                StartBackgroundRefresh(document, csvUrl, today, state.ETag, ct);
            }
        }

        var rows = await _store.GetRangeAsync(from, toDate, ct).ConfigureAwait(false);
        return new CsvPrayerTimesResult(
            rows,
            IsConfigured: true,
            CoversRequestedRange: CoversRange(rows, from, toDate),
            LastUpdatedAt: state.LastUpdatedAt);
    }

    private void StartBackgroundRefresh(
        SalahApiDocument document,
        string csvUrl,
        DateOnly today,
        string? etag,
        CancellationToken ct)
    {
        lock (_backgroundRefreshLock)
        {
            if (!_automaticallyRefreshedUrls.Add(csvUrl))
            {
                return;
            }
        }

        _ = RefreshInBackgroundAsync(document, csvUrl, today, etag, ct);
    }

    private async Task RefreshInBackgroundAsync(
        SalahApiDocument document,
        string csvUrl,
        DateOnly today,
        string? etag,
        CancellationToken ct)
    {
        try
        {
            await TryRefreshAsync(document, csvUrl, today, etag, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal when the initiating page or app lifecycle stops.
        }
#pragma warning disable CA1031 // Background refresh must never surface an unobserved exception.
        catch (Exception ex)
        {
            LogCsvFetchFailed(_logger, ex);
            MasjidTelemetry.DegradedOperations.Add(1, MasjidTelemetry.OperationTag("csv_fetch"));
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Decides whether to contact the feed. Coverage shortfall always wins over the daily
    /// throttle, so a device running out of days keeps retrying until it succeeds.
    /// </summary>
    private bool ShouldRefresh(CsvPrayerTimesFeedState state, DateOnly today, ApiCachePolicy cachePolicy)
    {
        if (state.MaxDate is not { } maxDate ||
            maxDate < today.AddDays(ForwardCoverageDays - 1))
        {
            return true;
        }

        if (cachePolicy == ApiCachePolicy.ForceRefresh)
        {
            return true;
        }

        return state.LastCheckedAt is not { } lastChecked ||
            _timeProvider.GetUtcNow() - lastChecked >= FreshnessCheckInterval;
    }

    /// <summary>
    /// Attempts one conditional download and, on success, replaces the stored rows.
    /// Every failure path leaves existing rows intact and records the check attempt so the
    /// daily cadence still advances (NFR-RL-01).
    /// </summary>
    private async Task TryRefreshAsync(
        SalahApiDocument document,
        string csvUrl,
        DateOnly today,
        string? etag,
        CancellationToken ct)
    {
        string url;
        try
        {
            url = BuildUrl(
                csvUrl,
                document.DailyPrayerTimes?.CsvUrlParameters,
                today,
                today.AddMonths(FetchWindowMonths));
        }
#pragma warning disable CA1031 // NFR-RL-01: degrade gracefully on URL-building failures; the exception is logged and metered below.
        catch (Exception ex)
        {
            LogUrlBuildFailed(_logger, csvUrl, ex);
            MasjidTelemetry.DegradedOperations.Add(1, MasjidTelemetry.OperationTag("csv_url_build"));
            return;
        }
#pragma warning restore CA1031

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (etag is { Length: > 0 })
            {
                // Sent unvalidated: feeds and proxies emit weak and malformed tags alike,
                // and the origin server is the only party that needs to interpret it.
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow();

            var currentState = await _store.GetStateAsync(ct).ConfigureAwait(false);
            if (!string.Equals(currentState.FeedUrl, csvUrl, StringComparison.Ordinal))
            {
                return;
            }

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                await _store.RecordCheckedAsync(csvUrl, etag, now, ct).ConfigureAwait(false);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogCsvStatusFailed(_logger, (int)response.StatusCode);
                MasjidTelemetry.DegradedOperations.Add(1, MasjidTelemetry.OperationTag("csv_fetch"));
                await _store.RecordCheckedAsync(csvUrl, etag, now, ct).ConfigureAwait(false);
                return;
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var rows = CsvPrayerTimesParser.Parse(content);
            if (rows.Count == 0)
            {
                // An empty or unparseable body must never wipe a usable stored schedule.
                LogCsvEmptyResponse(_logger);
                MasjidTelemetry.DegradedOperations.Add(1, MasjidTelemetry.OperationTag("csv_parse"));
                await _store.RecordCheckedAsync(csvUrl, etag, now, ct).ConfigureAwait(false);
                return;
            }

            await _store
                .ReplaceAllAsync(rows, csvUrl, response.Headers.ETag?.Tag, now, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // NFR-RL-01: degrade gracefully on network/parse/storage failures; the exception is logged and metered below.
        catch (Exception ex)
        {
            LogCsvFetchFailed(_logger, ex);
            MasjidTelemetry.DegradedOperations.Add(1, MasjidTelemetry.OperationTag("csv_fetch"));
            await RecordCheckAttemptAsync(csvUrl, etag, ct).ConfigureAwait(false);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Records a failed check attempt, tolerating a store that is itself unavailable.
    /// </summary>
    private async Task RecordCheckAttemptAsync(string csvUrl, string? etag, CancellationToken ct)
    {
        try
        {
            var state = await _store.GetStateAsync(ct).ConfigureAwait(false);
            if (!string.Equals(state.FeedUrl, csvUrl, StringComparison.Ordinal))
            {
                return;
            }

            await _store
                .RecordCheckedAsync(csvUrl, etag, _timeProvider.GetUtcNow(), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // NFR-RL-01: a store failure here must not mask the original network failure.
        catch (Exception ex)
        {
            LogCheckTimestampFailed(_logger, ex);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Returns <see langword="true"/> when every date in the range has a stored row.
    /// </summary>
    private static bool CoversRange(IReadOnlyList<CsvPrayerTimesRow> rows, DateOnly from, DateOnly toDate)
    {
        if (toDate < from)
        {
            return true;
        }

        if (rows.Count < toDate.DayNumber - from.DayNumber + 1)
        {
            return false;
        }

        var dates = rows.Select(r => r.Date).ToHashSet();
        for (var date = from; date <= toDate; date = date.AddDays(1))
        {
            if (!dates.Contains(date))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Constructs the final CSV URL by appending static parameters from
    /// <paramref name="csvUrlParameters"/> and the mandatory <c>fromDate</c> /
    /// <c>toDate</c> query values.
    /// </summary>
    internal static string BuildUrl(
        string baseUrl,
        JsonElement? csvUrlParameters,
        DateOnly from,
        DateOnly toDate)
    {
        var pairs = new List<(string Key, string Value)>();

        // Append any static scalar parameters defined in the document.
        if (csvUrlParameters is { ValueKind: JsonValueKind.Object } element)
        {
            CollectJsonObjectParams(element, pairs);
        }

        // Mandatory date-range parameters (appended last so they always win).
        pairs.Add(("fromDate", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        pairs.Add(("toDate", toDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));

        var sb = new StringBuilder(baseUrl);
        var separator = baseUrl.Contains('?') ? '&' : '?';
        foreach (var (key, value) in pairs)
        {
            sb.Append(separator);
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        return sb.ToString();
    }

    /// <summary>
    /// Collects scalar JSON properties from <paramref name="obj"/> into
    /// <paramref name="pairs"/>, skipping nested objects and arrays (NFR-RL-01).
    /// </summary>
    private static void CollectJsonObjectParams(
        JsonElement obj,
        List<(string Key, string Value)> pairs)
    {
        foreach (var property in obj.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    pairs.Add((property.Name, property.Value.GetString() ?? string.Empty));
                    break;
                case JsonValueKind.Number:
                    pairs.Add((property.Name, property.Value.GetRawText()));
                    break;
                case JsonValueKind.True:
                    pairs.Add((property.Name, "true"));
                    break;
                case JsonValueKind.False:
                    pairs.Add((property.Name, "false"));
                    break;
                // Skip nested objects, arrays, null - tolerant forward-compat (NFR-RL-01).
                default:
                    break;
            }
        }
    }

    // ── Source-generated high-performance log messages (CA1848) ─────────────────

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to build CSV prayer-times URL from '{CsvUrl}'; keeping stored rows.")]
    private static partial void LogUrlBuildFailed(ILogger logger, string csvUrl, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to download CSV prayer times; keeping stored rows.")]
    private static partial void LogCsvFetchFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CSV prayer-times feed returned HTTP {StatusCode}; keeping stored rows.")]
    private static partial void LogCsvStatusFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CSV prayer-times feed returned no parseable rows; keeping stored rows.")]
    private static partial void LogCsvEmptyResponse(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to record the CSV prayer-times check timestamp.")]
    private static partial void LogCheckTimestampFailed(ILogger logger, Exception ex);
}
