using System.Globalization;
using System.Net;
using FluentAssertions;
using MasjidApp.Core.Http;
using MasjidApp.Core.PrayerTimes;
using MasjidApp.Core.SalahApi;

namespace MasjidApp.Core.Tests.PrayerTimes;

// QA-02, API-03, FR-PT-04, NFR-OF-01, NFR-OF-03, NFR-RL-01:
// CSV prayer times are served from durable storage and refreshed conditionally.

public sealed class CsvPrayerTimesServiceTests
{
    private const string CsvUrl = "https://example.com/prayer-times.csv";
    private static readonly DateOnly Today = new(2026, 7, 29);
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    // ── Storage-first behaviour ────────────────────────────────────────────────

    [Fact]
    public async Task GetCsvTimesAsync_StoredRowsCoverWindowAndCheckedToday_MakesNoNetworkCall()
    {
        var store = new FakeStore();
        await SeedAsync(store, days: 60, etag: "\"v1\"", checkedAt: NowUtc.AddHours(-1));
        using var handler = new RecordingHandler(() => throw new InvalidOperationException("network not expected"));
        using var service = CreateService(handler, store);

        var result = await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        handler.RequestCount.Should().Be(0, "an adequately covered feed is only re-checked once a day");
        result.Rows.Should().HaveCount(30);
        result.CoversRequestedRange.Should().BeTrue();
    }

    [Fact]
    public async Task GetCsvTimesAsync_CheckDueAfterOneDay_SendsConditionalRequest()
    {
        var store = new FakeStore();
        await SeedAsync(store, days: 60, etag: "\"v1\"", checkedAt: NowUtc.AddHours(-25));
        using var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var service = CreateService(handler, store);

        await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        handler.RequestCount.Should().Be(1);
        handler.LastIfNoneMatch.Should().Be("\"v1\"", "the stored validator drives the freshness check");
    }

    [Fact]
    public async Task GetCsvTimesAsync_CoverageBelowThirtyDays_FetchesEvenWhenCheckedRecently()
    {
        var store = new FakeStore();
        await SeedAsync(store, days: 10, etag: "\"v1\"", checkedAt: NowUtc.AddMinutes(-5));
        using var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var service = CreateService(handler, store);

        await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        handler.RequestCount.Should().Be(1, "running out of future days always forces a fetch attempt");
    }

    [Fact]
    public async Task GetCsvTimesAsync_RefreshDue_ReturnsStoredRowsBeforeNetworkCompletes()
    {
        var store = new FakeStore();
        await SeedAsync(store, days: 60, etag: "\"v1\"", checkedAt: NowUtc.AddHours(-25));
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new AsyncRecordingHandler(async () =>
        {
            requestStarted.TrySetResult();
            await releaseRequest.Task;
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        using var http = new HttpClient(handler, disposeHandler: false);
        var service = new CsvPrayerTimesService(http, store, new FixedTimeProvider(NowUtc));

        var result = await service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        result.Rows.Should().HaveCount(30);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        releaseRequest.TrySetResult();
    }

    [Fact]
    public async Task GetCsvTimesAsync_ForceRefresh_BypassesDailyThrottle()
    {
        var store = new FakeStore();
        await SeedAsync(store, days: 60, etag: "\"v1\"", checkedAt: NowUtc.AddMinutes(-5));
        using var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var service = CreateService(handler, store);

        await service.Service.GetCsvTimesAsync(
            Document(), Today, Today.AddDays(29), ApiCachePolicy.ForceRefresh);

        handler.RequestCount.Should().Be(1);
    }

    // ── Refresh outcomes ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetCsvTimesAsync_SuccessfulDownload_ReplacesRowsAndStoresETag()
    {
        var store = new FakeStore();
        using var handler = new RecordingHandler(() => CsvResponse(BuildCsv(Today, 40), "\"v2\""));
        using var service = CreateService(handler, store);

        var result = await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        result.Rows.Should().HaveCount(30);
        result.CoversRequestedRange.Should().BeTrue();

        var state = await store.GetStateAsync();
        state.ETag.Should().Be("\"v2\"");
        state.FeedUrl.Should().Be(CsvUrl);
        state.RowCount.Should().Be(40);
        state.LastUpdatedAt.Should().Be(NowUtc);
    }

    [Fact]
    public async Task GetCsvTimesAsync_RequestsSixMonthsStartingToday()
    {
        var store = new FakeStore();
        using var handler = new RecordingHandler(() => CsvResponse(BuildCsv(Today, 40), "\"v2\""));
        using var service = CreateService(handler, store);

        await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        handler.LastRequestUri.Should().Contain("fromDate=2026-07-29");
        handler.LastRequestUri.Should().Contain("toDate=2027-01-29");
    }

    [Fact]
    public async Task GetCsvTimesAsync_NotModified_KeepsStoredRows()
    {
        var store = new FakeStore();
        await SeedAsync(store, days: 60, etag: "\"v1\"", checkedAt: NowUtc.AddHours(-25));
        using var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var service = CreateService(handler, store);

        var result = await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        result.Rows.Should().HaveCount(30);
        (await store.GetStateAsync()).RowCount.Should().Be(60);
        store.ReplaceCount.Should().Be(0, "304 must not rewrite the table");
    }

    [Fact]
    public async Task GetCsvTimesAsync_NetworkFailure_KeepsStoredRowsAndDoesNotThrow()
    {
        var store = new FakeStore();
        await SeedAsync(store, days: 60, etag: "\"v1\"", checkedAt: NowUtc.AddHours(-25));
        using var handler = new RecordingHandler(() => throw new HttpRequestException("offline"));
        using var service = CreateService(handler, store);

        var result = await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        result.Rows.Should().HaveCount(30, "stored rows survive a failed refresh");
        result.CoversRequestedRange.Should().BeTrue();
        (await store.GetStateAsync()).RowCount.Should().Be(60);
    }

    [Fact]
    public async Task GetCsvTimesAsync_EmptyResponseBody_DoesNotDestroyStoredRows()
    {
        var store = new FakeStore();
        await SeedAsync(store, days: 60, etag: "\"v1\"", checkedAt: NowUtc.AddHours(-25));
        using var handler = new RecordingHandler(() => CsvResponse(string.Empty, "\"v2\""));
        using var service = CreateService(handler, store);

        var result = await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        result.Rows.Should().HaveCount(30);
        store.ReplaceCount.Should().Be(0, "an unparseable body must never truncate a usable schedule");
    }

    [Fact]
    public async Task GetCsvTimesAsync_ServerError_KeepsStoredRows()
    {
        var store = new FakeStore();
        await SeedAsync(store, days: 60, etag: "\"v1\"", checkedAt: NowUtc.AddHours(-25));
        using var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var service = CreateService(handler, store);

        var result = await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        result.Rows.Should().HaveCount(30);
        store.ReplaceCount.Should().Be(0);
    }

    // ── Coverage reporting and isolation ───────────────────────────────────────

    [Fact]
    public async Task GetCsvTimesAsync_GapInStoredRange_ReportsIncompleteCoverage()
    {
        var store = new FakeStore();
        await SeedAsync(store, days: 60, etag: "\"v1\"", checkedAt: NowUtc.AddHours(-1));
        store.Remove(Today.AddDays(5));
        using var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var service = CreateService(handler, store);

        var result = await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        result.IsConfigured.Should().BeTrue();
        result.CoversRequestedRange.Should().BeFalse();
        result.IsDegraded.Should().BeTrue("a missing day means calculated estimates are shown");
    }

    [Fact]
    public async Task GetCsvTimesAsync_NoCsvUrlConfigured_ReportsNotConfiguredWithoutNetwork()
    {
        var store = new FakeStore();
        using var handler = new RecordingHandler(() => throw new InvalidOperationException("network not expected"));
        using var service = CreateService(handler, store);

        var result = await service.Service.GetCsvTimesAsync(
            new SalahApiDocument(), Today, Today.AddDays(29));

        result.IsConfigured.Should().BeFalse();
        result.IsDegraded.Should().BeFalse("calculated times are the intended source when no feed exists");
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCsvTimesAsync_FeedUrlChanged_DiscardsRowsFromThePreviousMasjid()
    {
        var store = new FakeStore();
        await SeedAsync(
            store, days: 60, etag: "\"v1\"", checkedAt: NowUtc,
            feedUrl: "https://other.example.com/times.csv");
        using var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var service = CreateService(handler, store);

        var result = await service.Service.GetCsvTimesAsync(Document(), Today, Today.AddDays(29));

        result.Rows.Should().BeEmpty("rows downloaded for another masjid must never be served");
        handler.LastIfNoneMatch.Should().BeNull("the previous masjid's validator does not apply");
    }

    [Fact]
    public async Task GetCsvTimesAsync_CallerCancellation_RethrowsCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var store = new FakeStore();
        using var handler = new RecordingHandler(() => throw new OperationCanceledException(cts.Token));
        using var service = CreateService(handler, store);

        var act = () => service.Service.GetCsvTimesAsync(
            Document(), Today, Today.AddDays(29), ApiCachePolicy.PreferCache, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ServiceFixture CreateService(RecordingHandler handler, FakeStore store)
    {
        var http = new HttpClient(handler, disposeHandler: false);
        return new ServiceFixture(
            http,
            new CsvPrayerTimesService(http, store, new FixedTimeProvider(NowUtc)));
    }

    private sealed record ServiceFixture(HttpClient Http, CsvPrayerTimesService Service) : IDisposable
    {
        public void Dispose() => Http.Dispose();
    }

    private static SalahApiDocument Document() => new()
    {
        Location = new SalahApiLocation { Timezone = "UTC" },
        DailyPrayerTimes = new SalahApiDailyPrayerTimes { CsvUrl = CsvUrl },
    };

    private static async Task SeedAsync(
        FakeStore store,
        int days,
        string? etag,
        DateTimeOffset checkedAt,
        string feedUrl = CsvUrl)
    {
        var rows = Enumerable.Range(0, days)
            .Select(offset => MakeRow(Today.AddDays(offset)))
            .ToList();
        await store.ReplaceAllAsync(rows, feedUrl, etag, checkedAt);
        store.ReplaceCount = 0;
    }

    private static CsvPrayerTimesRow MakeRow(DateOnly date) => new(
        date,
        new TimeOnly(4, 30),
        new TimeOnly(6, 0),
        new TimeOnly(13, 10),
        new TimeOnly(17, 5),
        new TimeOnly(20, 45),
        new TimeOnly(22, 30));

    private static string BuildCsv(DateOnly from, int days)
    {
        var lines = new List<string>
        {
            "day,fajr_athan,fajr_iqama,sunrise,dhuhr_athan,dhuhr_iqama,asr_athan,asr_iqama,maghrib_athan,maghrib_iqama,isha_athan,isha_iqama",
        };

        for (var offset = 0; offset < days; offset++)
        {
            var date = from.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            lines.Add($"{date},04:30:00,05:00:00,06:00:00,13:10:00,13:30:00,17:05:00,17:30:00,20:45:00,20:55:00,22:30:00,22:45:00");
        }

        return string.Join('\n', lines);
    }

    private static HttpResponseMessage CsvResponse(string body, string? etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        };

        if (etag is not null)
        {
            response.Headers.TryAddWithoutValidation("ETag", etag);
        }

        return response;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingHandler(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public string? LastRequestUri { get; private set; }

        public string? LastIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri?.ToString();
            LastIfNoneMatch = request.Headers.TryGetValues("If-None-Match", out var values)
                ? values.FirstOrDefault()
                : null;

            try
            {
                return Task.FromResult(responder());
            }
#pragma warning disable CA1031 // Test double relays whatever the scenario throws to the caller.
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
#pragma warning restore CA1031
        }
    }

    private sealed class AsyncRecordingHandler(Func<Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder();
    }

    /// <summary>In-memory <see cref="ICsvPrayerTimesStore"/> mirroring the SQLite semantics.</summary>
    private sealed class FakeStore : ICsvPrayerTimesStore
    {
        private readonly SortedDictionary<DateOnly, CsvPrayerTimesRow> _rows = [];
        private CsvPrayerTimesFeedState _state = CsvPrayerTimesFeedState.Empty;

        public int ReplaceCount { get; set; }

        public void Remove(DateOnly date) => _rows.Remove(date);

        public Task<CsvPrayerTimesFeedState> GetStateAsync(CancellationToken ct = default) =>
            Task.FromResult(_state with
            {
                MaxDate = _rows.Count > 0 ? _rows.Keys.Max() : null,
                RowCount = _rows.Count,
            });

        public Task<IReadOnlyList<CsvPrayerTimesRow>> GetRangeAsync(
            DateOnly from,
            DateOnly toDate,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CsvPrayerTimesRow>>(
                _rows.Where(kv => kv.Key >= from && kv.Key <= toDate).Select(kv => kv.Value).ToList());

        public Task ReplaceAllAsync(
            IReadOnlyList<CsvPrayerTimesRow> rows,
            string feedUrl,
            string? etag,
            DateTimeOffset fetchedAt,
            CancellationToken ct = default)
        {
            ReplaceCount++;
            _rows.Clear();
            foreach (var row in rows)
            {
                _rows[row.Date] = row;
            }

            _state = new CsvPrayerTimesFeedState(feedUrl, etag, fetchedAt, fetchedAt, null, 0);
            return Task.CompletedTask;
        }

        public Task RecordCheckedAsync(
            string feedUrl,
            string? etag,
            DateTimeOffset checkedAt,
            CancellationToken ct = default)
        {
            _state = _state with { FeedUrl = feedUrl, ETag = etag, LastCheckedAt = checkedAt };
            return Task.CompletedTask;
        }

        public Task ResetAsync(string feedUrl, CancellationToken ct = default)
        {
            _rows.Clear();
            _state = CsvPrayerTimesFeedState.Empty with { FeedUrl = feedUrl };
            return Task.CompletedTask;
        }
    }
}
