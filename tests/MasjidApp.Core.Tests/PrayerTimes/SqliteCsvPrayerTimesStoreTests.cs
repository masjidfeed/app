using FluentAssertions;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Tests.PrayerTimes;

// QA-02, API-03, NFR-OF-03: daily CSV rows and their feed validator survive app restarts
// so the published schedule renders without a network call.

public sealed class SqliteCsvPrayerTimesStoreTests : IDisposable
{
    private const string FeedUrl = "https://example.com/prayer-times.csv";
    private static readonly DateOnly Day0 = new(2026, 7, 29);
    private static readonly DateTimeOffset FetchedAt = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"csvtest_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private SqliteCsvPrayerTimesStore CreateStore() => new(_dbPath);

    [Fact]
    public async Task GetStateAsync_EmptyDatabase_ReturnsEmptyState()
    {
        using var store = CreateStore();

        var state = await store.GetStateAsync();

        state.Should().Be(CsvPrayerTimesFeedState.Empty);
    }

    [Fact]
    public async Task GetRangeAsync_EmptyDatabase_ReturnsNoRows()
    {
        using var store = CreateStore();

        var rows = await store.GetRangeAsync(Day0, Day0.AddDays(30));

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplaceAllAsync_ThenGetRangeAsync_RoundTripsEveryColumn()
    {
        using var store = CreateStore();
        var row = new CsvPrayerTimesRow(
            Day0,
            Fajr: new TimeOnly(4, 22),
            Sunrise: new TimeOnly(5, 55),
            Dhuhr: new TimeOnly(13, 8),
            Asr: new TimeOnly(17, 1),
            Maghrib: new TimeOnly(20, 40),
            Isha: new TimeOnly(22, 22),
            FajrIqama: new TimeOnly(4, 45),
            DhuhrIqama: new TimeOnly(13, 30),
            AsrIqama: null,
            MaghribIqama: new TimeOnly(20, 50),
            IshaIqama: new TimeOnly(22, 45));

        await store.ReplaceAllAsync([row], FeedUrl, "\"v1\"", FetchedAt);
        var rows = await store.GetRangeAsync(Day0, Day0);

        rows.Should().ContainSingle().Which.Should().Be(row);
    }

    [Fact]
    public async Task GetRangeAsync_BoundedRange_ReturnsOnlyRequestedDaysInOrder()
    {
        using var store = CreateStore();
        await store.ReplaceAllAsync(MakeRows(Day0, 10), FeedUrl, "\"v1\"", FetchedAt);

        var rows = await store.GetRangeAsync(Day0.AddDays(2), Day0.AddDays(5));

        rows.Should().HaveCount(4);
        rows.Select(r => r.Date).Should().ContainInOrder(
            Day0.AddDays(2), Day0.AddDays(3), Day0.AddDays(4), Day0.AddDays(5));
    }

    [Fact]
    public async Task ReplaceAllAsync_SecondCall_DiscardsThePreviousSchedule()
    {
        using var store = CreateStore();
        await store.ReplaceAllAsync(MakeRows(Day0, 10), FeedUrl, "\"v1\"", FetchedAt);

        var newStart = Day0.AddDays(100);
        await store.ReplaceAllAsync(MakeRows(newStart, 5), FeedUrl, "\"v2\"", FetchedAt.AddDays(1));

        (await store.GetRangeAsync(Day0, Day0.AddDays(10))).Should().BeEmpty();
        var state = await store.GetStateAsync();
        state.RowCount.Should().Be(5);
        state.ETag.Should().Be("\"v2\"");
        state.MaxDate.Should().Be(newStart.AddDays(4));
    }

    [Fact]
    public async Task GetStateAsync_AfterReplaceAll_ReportsCoverageAndTimestamps()
    {
        using var store = CreateStore();

        await store.ReplaceAllAsync(MakeRows(Day0, 30), FeedUrl, "\"v1\"", FetchedAt);
        var state = await store.GetStateAsync();

        state.FeedUrl.Should().Be(FeedUrl);
        state.ETag.Should().Be("\"v1\"");
        state.LastCheckedAt.Should().Be(FetchedAt);
        state.LastUpdatedAt.Should().Be(FetchedAt);
        state.MaxDate.Should().Be(Day0.AddDays(29));
        state.RowCount.Should().Be(30);
    }

    [Fact]
    public async Task RecordCheckedAsync_KeepsRowsAndAdvancesTheCheckTimestamp()
    {
        using var store = CreateStore();
        await store.ReplaceAllAsync(MakeRows(Day0, 30), FeedUrl, "\"v1\"", FetchedAt);

        var checkedAt = FetchedAt.AddDays(1);
        await store.RecordCheckedAsync(FeedUrl, "\"v1\"", checkedAt);

        var state = await store.GetStateAsync();
        state.LastCheckedAt.Should().Be(checkedAt);
        state.LastUpdatedAt.Should().Be(FetchedAt, "a 304 does not change the payload timestamp");
        state.RowCount.Should().Be(30);
    }

    [Fact]
    public async Task ResetAsync_ClearsRowsAndValidatorForTheNewFeed()
    {
        using var store = CreateStore();
        await store.ReplaceAllAsync(MakeRows(Day0, 30), FeedUrl, "\"v1\"", FetchedAt);

        const string OtherFeed = "https://other.example.com/times.csv";
        await store.ResetAsync(OtherFeed);

        var state = await store.GetStateAsync();
        state.FeedUrl.Should().Be(OtherFeed);
        state.ETag.Should().BeNull("API-06: another masjid's validator must not be reused");
        state.RowCount.Should().Be(0);
        state.LastCheckedAt.Should().BeNull();
    }

    [Fact]
    public async Task ReplaceAllAsync_ThenNewConnection_DataSurvivesAppRestart()
    {
        using (var store = CreateStore())
        {
            await store.ReplaceAllAsync(MakeRows(Day0, 30), FeedUrl, "\"v1\"", FetchedAt);
            await store.CloseAsync();
        }

        using var reopened = CreateStore();

        (await reopened.GetRangeAsync(Day0, Day0.AddDays(29))).Should().HaveCount(30);
        (await reopened.GetStateAsync()).ETag.Should().Be("\"v1\"");
    }

    [Fact]
    public async Task CloseAsync_ThenReuse_ReopensTheDatabase()
    {
        using var store = CreateStore();
        await store.ReplaceAllAsync(MakeRows(Day0, 5), FeedUrl, "\"v1\"", FetchedAt);

        await store.CloseAsync();

        (await store.GetRangeAsync(Day0, Day0.AddDays(4))).Should().HaveCount(5);
    }

    private static List<CsvPrayerTimesRow> MakeRows(DateOnly from, int days) =>
        [.. Enumerable.Range(0, days).Select(offset => new CsvPrayerTimesRow(
            from.AddDays(offset),
            new TimeOnly(4, 30),
            new TimeOnly(6, 0),
            new TimeOnly(13, 10),
            new TimeOnly(17, 5),
            new TimeOnly(20, 45),
            new TimeOnly(22, 30)))];
}
