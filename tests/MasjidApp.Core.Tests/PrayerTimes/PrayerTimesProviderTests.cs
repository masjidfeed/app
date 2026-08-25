using FluentAssertions;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;
using MasjidApp.Core.PrayerTimes;
using MasjidApp.Core.SalahApi;
using MasjidApp.Core.Services;

namespace MasjidApp.Core.Tests.PrayerTimes;

// QA-02, FR-PT-01, FR-PT-09, NFR-RL-01, NFR-OF-01: unit tests for PrayerTimesProvider.

public sealed class PrayerTimesProviderTests
{
    // ── Fixtures ───────────────────────────────────────────────────────────────

    private static readonly DateOnly Today = new(2026, 6, 1);
    private static readonly DateTimeOffset TestNowUtc = new(2026, 6, 1, 19, 0, 0, TimeSpan.Zero);
    private const string SalahApiUrl = "https://masjid.example.com/salahapi";

    private static SalahApiDocument MakeDocument() => new()
    {
        Location = new SalahApiLocation
        {
            Latitude = 47.76,
            Longitude = -122.21,
            Timezone = "America/Los_Angeles",
        },
        CalculationMethod = new SalahApiCalculationMethod
        {
            Name = "ISNA",
            AsrCalculationMethod = "Standard",
            HighLatitudeAdjustment = "AngleBased",
        },
    };

    private static DailyPrayerSchedule MakeSchedule(DateOnly date) => new()
    {
        Date = date,
        Timezone = "America/Los_Angeles",
        FajrAthan = new TimeOnly(4, 30),
        Sunrise = new TimeOnly(6, 0),
        DhuhrAthan = new TimeOnly(13, 10),
        AsrAthan = new TimeOnly(17, 5),
        MaghribAthan = new TimeOnly(20, 45),
        IshaAthan = new TimeOnly(22, 30),
    };

    private static MasjidConfig MakeConfig() => new()
    {
        Endpoints = new EndpointsConfig { SalahApiUrl = SalahApiUrl },
    };

    // ── Fakes ──────────────────────────────────────────────────────────────────

    private sealed class FakeMasjidContext : IMasjidContext
    {
        private readonly MasjidConfig? _config;

        public FakeMasjidContext(MasjidConfig? config = null) =>
            _config = config ?? MakeConfig();

        public MasjidStoredEntry? ActiveEntry => null;

        public event EventHandler? ActiveMasjidChanged;

        public Task<ApiResult<MasjidConfig>?> GetConfigAsync(
            ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
            CancellationToken ct = default)
        {
            if (_config is null)
            {
                return Task.FromResult<ApiResult<MasjidConfig>?>(null);
            }

            return Task.FromResult<ApiResult<MasjidConfig>?>(
                new ApiResult<MasjidConfig>.Fresh(_config, DateTimeOffset.UtcNow));
        }

        public void SetActive(MasjidStoredEntry entry) =>
            ActiveMasjidChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeSalahApiService : ISalahApiService
    {
        private readonly ApiResult<SalahApiDocument> _result;

        public FakeSalahApiService(ApiResult<SalahApiDocument> result) => _result = result;

        public Task<ApiResult<SalahApiDocument>> GetDocumentAsync(
            string salahApiUrl,
            ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
            CancellationToken ct = default) =>
            Task.FromResult(_result);
    }

    private sealed class FakePrayerTimeCalculator : IPrayerTimeCalculator
    {
        public DailyPrayerSchedule Calculate(SalahApiDocument document, DateOnly targetDate) =>
            MakeSchedule(targetDate);
    }

    private sealed class FakeCsvPrayerTimesService : ICsvPrayerTimesService
    {
        private readonly CsvPrayerTimesResult _result;

        public FakeCsvPrayerTimesService(
            IReadOnlyList<CsvPrayerTimesRow>? rows = null,
            bool coversRequestedRange = true) =>
            _result = rows is null
                ? CsvPrayerTimesResult.NotConfigured
                : new CsvPrayerTimesResult(rows, true, coversRequestedRange, TestNowUtc);

        public Task<CsvPrayerTimesResult> GetCsvTimesAsync(
            SalahApiDocument document,
            DateOnly from,
            DateOnly toDate,
            ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
            CancellationToken ct = default) =>
            Task.FromResult(_result);
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTodayAsync_FreshDocument_ReturnsFreshDailySchedule()
    {
        // Arrange
        var freshResult = new ApiResult<SalahApiDocument>.Fresh(MakeDocument(), DateTimeOffset.UtcNow);
        var provider = new PrayerTimesProvider(
            new FakeMasjidContext(),
            new FakeSalahApiService(freshResult),
            new FakePrayerTimeCalculator(),
            new FakeCsvPrayerTimesService(),
            new TestTimeProvider(TestNowUtc));

        // Act
        var result = await provider.GetTodayAsync();

        // Assert
        result.Should().BeOfType<ApiResult<DailyPrayerSchedule>.Fresh>();
        var fresh = (ApiResult<DailyPrayerSchedule>.Fresh)result;
        fresh.Value.Date.Should().Be(Today);
        fresh.Value.FajrAthan.Should().Be(new TimeOnly(4, 30));
    }

    [Fact]
    public async Task GetTodayAsync_StaleDocument_ReturnsStaleSchedule()
    {
        // Arrange - simulate a stale cache (network failed but cache is available).
        var staleResult = new ApiResult<SalahApiDocument>.Stale(
            MakeDocument(),
            DateTimeOffset.UtcNow.AddHours(-25),
            new HttpRequestException("network error"));

        var provider = new PrayerTimesProvider(
            new FakeMasjidContext(),
            new FakeSalahApiService(staleResult),
            new FakePrayerTimeCalculator(),
            new FakeCsvPrayerTimesService(),
            new TestTimeProvider(TestNowUtc));

        // Act
        var result = await provider.GetTodayAsync();

        // Assert - result is Stale, data is still available.
        result.Should().BeOfType<ApiResult<DailyPrayerSchedule>.Stale>();
        result.HasValue.Should().BeTrue();
        result.ValueOrDefault!.Date.Should().Be(Today);
    }

    [Fact]
    public async Task GetTodayAsync_UtcDateAheadOfMasjidDate_ReturnsMasjidLocalDate()
    {
        var utcNow = new DateTimeOffset(2026, 6, 2, 6, 30, 0, TimeSpan.Zero);
        var freshResult = new ApiResult<SalahApiDocument>.Fresh(MakeDocument(), utcNow);
        var provider = new PrayerTimesProvider(
            new FakeMasjidContext(),
            new FakeSalahApiService(freshResult),
            new FakePrayerTimeCalculator(),
            new FakeCsvPrayerTimesService(),
            new TestTimeProvider(utcNow));

        var result = await provider.GetTodayAsync();

        result.ValueOrDefault!.Date.Should().Be(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public async Task GetTodayAsync_CsvRowAvailable_AthanTimesOverriddenByCSV()
    {
        // Arrange - CSV provides different athan times from the calculator.
        var csvFajrOverride = new TimeOnly(4, 22);
        var csvRow = new CsvPrayerTimesRow(
            Date: Today,
            Fajr: csvFajrOverride,
            Sunrise: new TimeOnly(5, 55),
            Dhuhr: new TimeOnly(13, 8),
            Asr: new TimeOnly(17, 1),
            Maghrib: new TimeOnly(20, 40),
            Isha: new TimeOnly(22, 22));

        var freshResult = new ApiResult<SalahApiDocument>.Fresh(MakeDocument(), DateTimeOffset.UtcNow);
        var provider = new PrayerTimesProvider(
            new FakeMasjidContext(),
            new FakeSalahApiService(freshResult),
            new FakePrayerTimeCalculator(),
            new FakeCsvPrayerTimesService([csvRow]),
            new TestTimeProvider(TestNowUtc));

        // Act
        var result = await provider.GetTodayAsync();

        // Assert - CSV athan wins; calculator value (4:30) is replaced by CSV value (4:22).
        result.Should().BeOfType<ApiResult<DailyPrayerSchedule>.Fresh>();
        result.ValueOrDefault!.FajrAthan.Should().Be(csvFajrOverride,
            "CSV athan times override calculated times (API-04 merge policy)");
        result.ValueOrDefault.MaghribAthan.Should().Be(new TimeOnly(20, 40));
    }

    [Fact]
    public async Task GetTodayAsync_ConfiguredCsvDoesNotCoverWindow_ReportsStale()
    {
        // Arrange - CSV feed is configured but cannot cover the rendered window, so the
        // displayed times are calculated estimates and must not pass for fresh data.
        var freshResult = new ApiResult<SalahApiDocument>.Fresh(MakeDocument(), DateTimeOffset.UtcNow);
        var provider = new PrayerTimesProvider(
            new FakeMasjidContext(),
            new FakeSalahApiService(freshResult),
            new FakePrayerTimeCalculator(),
            new FakeCsvPrayerTimesService([], coversRequestedRange: false),
            new TestTimeProvider(TestNowUtc));

        // Act
        var result = await provider.GetTodayAsync();

        // Assert
        result.Should().BeOfType<ApiResult<DailyPrayerSchedule>.Stale>(
            "FR-PT-04: falling back to calculated times must be visible, not silent");
        result.ValueOrDefault.Should().NotBeNull("times still render while degraded");
    }

    [Fact]
    public async Task GetTodayAsync_NoCsvConfigured_RemainsFresh()
    {
        // Arrange - no CSV feed configured; calculated times are the intended source.
        var freshResult = new ApiResult<SalahApiDocument>.Fresh(MakeDocument(), DateTimeOffset.UtcNow);
        var provider = new PrayerTimesProvider(
            new FakeMasjidContext(),
            new FakeSalahApiService(freshResult),
            new FakePrayerTimeCalculator(),
            new FakeCsvPrayerTimesService(),
            new TestTimeProvider(TestNowUtc));

        // Act
        var result = await provider.GetTodayAsync();

        // Assert
        result.Should().BeOfType<ApiResult<DailyPrayerSchedule>.Fresh>();
    }

    [Fact]
    public async Task GetTodayAsync_NoSalahApiUrl_ReturnsFailed()
    {
        // Arrange - context returns config without a SalahAPI URL.
        var configWithoutUrl = new MasjidConfig { Endpoints = new EndpointsConfig { SalahApiUrl = null } };
        var provider = new PrayerTimesProvider(
            new FakeMasjidContext(configWithoutUrl),
            new FakeSalahApiService(new ApiResult<SalahApiDocument>.Failed(null, "unused")),
            new FakePrayerTimeCalculator(),
            new FakeCsvPrayerTimesService(),
            new TestTimeProvider(TestNowUtc));

        // Act
        var result = await provider.GetTodayAsync();

        // Assert
        result.Should().BeOfType<ApiResult<DailyPrayerSchedule>.Failed>();
    }

    [Fact]
    public async Task GetRangeAsync_ValidDocumentAndRange_ReturnsSchedulePerDay()
    {
        // Arrange
        var from = Today;
        var to = Today.AddDays(6);
        var freshResult = new ApiResult<SalahApiDocument>.Fresh(MakeDocument(), DateTimeOffset.UtcNow);
        var provider = new PrayerTimesProvider(
            new FakeMasjidContext(),
            new FakeSalahApiService(freshResult),
            new FakePrayerTimeCalculator(),
            new FakeCsvPrayerTimesService(),
            new TestTimeProvider(TestNowUtc));

        // Act
        var range = await provider.GetRangeAsync(from, to);

        // Assert - 7 schedules, dates are consecutive.
        range.Should().HaveCount(7);
        for (var i = 0; i < 7; i++)
        {
            range[i].Date.Should().Be(from.AddDays(i));
        }
    }

    [Fact]
    public async Task GetCurrentRangeAsync_UtcDateAheadOfMasjidDate_StartsOnMasjidLocalDate()
    {
        var utcNow = new DateTimeOffset(2026, 6, 2, 6, 30, 0, TimeSpan.Zero);
        var freshResult = new ApiResult<SalahApiDocument>.Fresh(MakeDocument(), utcNow);
        var provider = new PrayerTimesProvider(
            new FakeMasjidContext(),
            new FakeSalahApiService(freshResult),
            new FakePrayerTimeCalculator(),
            new FakeCsvPrayerTimesService(),
            new TestTimeProvider(utcNow));

        var range = await provider.GetCurrentRangeAsync(7);

        range.Should().HaveCount(7);
        range[0].Date.Should().Be(new DateOnly(2026, 6, 1));
        range[^1].Date.Should().Be(new DateOnly(2026, 6, 7));
    }
}
