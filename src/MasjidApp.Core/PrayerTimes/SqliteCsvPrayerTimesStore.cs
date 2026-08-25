using System.Globalization;
using MasjidApp.Core.Caching;
using SQLite;

namespace MasjidApp.Core.PrayerTimes;

// API-03, FR-PT-04, NFR-OF-03: SQLite-backed implementation of ICsvPrayerTimesStore.

/// <summary>
/// SQLite-backed implementation of <see cref="ICsvPrayerTimesStore"/>.
/// Stores one row per day, mirroring the CSV feed's own schema, so today's schedule and
/// the forward 30-day window render without any network access.
/// </summary>
/// <remarks>
/// Dates and times are persisted as invariant <c>yyyy-MM-dd</c> / <c>HH:mm:ss</c> strings.
/// ISO-8601 dates sort and compare lexicographically, which keeps range queries index-friendly
/// without depending on SQLite date functions.
/// </remarks>
public sealed class SqliteCsvPrayerTimesStore : ICsvPrayerTimesStore, ICacheStoreLifecycle, IDisposable
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm:ss";

    private readonly string _databasePath;
    private SQLiteAsyncConnection? _db;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    /// <summary>
    /// Initialises the store. The database file is not opened until the first operation.
    /// </summary>
    /// <param name="databasePath">Full file-system path to the SQLite database file.</param>
    public SqliteCsvPrayerTimesStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    /// <inheritdoc/>
    public async Task<CsvPrayerTimesFeedState> GetStateAsync(CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = await GetDatabaseAsync().ConfigureAwait(false);
            var feed = await db.Table<FeedRow>()
                .Where(r => r.Id == FeedRow.SingletonId)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (feed is null)
            {
                return CsvPrayerTimesFeedState.Empty;
            }

            var rowCount = await db
                .ExecuteScalarAsync<int>("select count(*) from csv_prayer_times")
                .ConfigureAwait(false);
            var maxDay = await db
                .ExecuteScalarAsync<string?>("select max(Day) from csv_prayer_times")
                .ConfigureAwait(false);

            return new CsvPrayerTimesFeedState(
                feed.FeedUrl,
                feed.ETag,
                ToTimestamp(feed.LastCheckedAtTicks),
                ToTimestamp(feed.LastUpdatedAtTicks),
                ParseDate(maxDay),
                rowCount);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CsvPrayerTimesRow>> GetRangeAsync(
        DateOnly from,
        DateOnly toDate,
        CancellationToken ct = default)
    {
        if (toDate < from)
        {
            return [];
        }

        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = await GetDatabaseAsync().ConfigureAwait(false);
            var rows = await db.QueryAsync<DayRow>(
                "select * from csv_prayer_times where Day >= ? and Day <= ? order by Day",
                FormatDate(from),
                FormatDate(toDate)).ConfigureAwait(false);

            var result = new List<CsvPrayerTimesRow>(rows.Count);
            foreach (var row in rows)
            {
                if (row.ToDomain() is { } parsed)
                {
                    result.Add(parsed);
                }
            }

            return result;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ReplaceAllAsync(
        IReadOnlyList<CsvPrayerTimesRow> rows,
        string feedUrl,
        string? etag,
        DateTimeOffset fetchedAt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);

        var dayRows = rows.Select(DayRow.FromDomain).ToList();
        var feed = new FeedRow
        {
            Id = FeedRow.SingletonId,
            FeedUrl = feedUrl,
            ETag = etag,
            LastCheckedAtTicks = fetchedAt.UtcTicks,
            LastUpdatedAtTicks = fetchedAt.UtcTicks,
        };

        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = await GetDatabaseAsync().ConfigureAwait(false);

            // One transaction so a crash mid-write cannot leave a half-populated schedule.
            await db.RunInTransactionAsync(connection =>
            {
                connection.DeleteAll<DayRow>();
                connection.InsertAll(dayRows);
                connection.InsertOrReplace(feed);
            }).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task RecordCheckedAsync(
        string feedUrl,
        string? etag,
        DateTimeOffset checkedAt,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);

        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = await GetDatabaseAsync().ConfigureAwait(false);
            var feed = await db.Table<FeedRow>()
                .Where(r => r.Id == FeedRow.SingletonId)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false) ?? new FeedRow { Id = FeedRow.SingletonId };

            feed.FeedUrl = feedUrl;
            feed.ETag = etag;
            feed.LastCheckedAtTicks = checkedAt.UtcTicks;
            await db.InsertOrReplaceAsync(feed).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ResetAsync(string feedUrl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);

        var feed = new FeedRow { Id = FeedRow.SingletonId, FeedUrl = feedUrl };

        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = await GetDatabaseAsync().ConfigureAwait(false);
            await db.RunInTransactionAsync(connection =>
            {
                connection.DeleteAll<DayRow>();
                connection.InsertOrReplace(feed);
            }).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = _db;
            _db = null;
            if (db is not null)
            {
                await db.CloseAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        if (_db is null)
        {
            var connection = new SQLiteAsyncConnection(_databasePath);
            await connection.CreateTableAsync<DayRow>().ConfigureAwait(false);
            await connection.CreateTableAsync<FeedRow>().ConfigureAwait(false);
            _db = connection;
        }

        return _db;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _operationLock.Dispose();
    }

    // ── Conversion helpers ──────────────────────────────────────────────────────

    private static string FormatDate(DateOnly value) =>
        value.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static string FormatTime(TimeOnly value) =>
        value.ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? ToTimestamp(long ticks) =>
        ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    // ── Table models ────────────────────────────────────────────────────────────

    /// <summary>One stored day, mirroring the CSV feed's column layout.</summary>
    [Table("csv_prayer_times")]
    private sealed class DayRow
    {
        [PrimaryKey]
        public string Day { get; set; } = string.Empty;

        public string FajrAthan { get; set; } = string.Empty;

        public string? FajrIqama { get; set; }

        public string Sunrise { get; set; } = string.Empty;

        public string DhuhrAthan { get; set; } = string.Empty;

        public string? DhuhrIqama { get; set; }

        public string AsrAthan { get; set; } = string.Empty;

        public string? AsrIqama { get; set; }

        public string MaghribAthan { get; set; } = string.Empty;

        public string? MaghribIqama { get; set; }

        public string IshaAthan { get; set; } = string.Empty;

        public string? IshaIqama { get; set; }

        public static DayRow FromDomain(CsvPrayerTimesRow row) => new()
        {
            Day = FormatDate(row.Date),
            FajrAthan = FormatTime(row.Fajr),
            FajrIqama = row.FajrIqama is { } fi ? FormatTime(fi) : null,
            Sunrise = FormatTime(row.Sunrise),
            DhuhrAthan = FormatTime(row.Dhuhr),
            DhuhrIqama = row.DhuhrIqama is { } di ? FormatTime(di) : null,
            AsrAthan = FormatTime(row.Asr),
            AsrIqama = row.AsrIqama is { } ai ? FormatTime(ai) : null,
            MaghribAthan = FormatTime(row.Maghrib),
            MaghribIqama = row.MaghribIqama is { } mi ? FormatTime(mi) : null,
            IshaAthan = FormatTime(row.Isha),
            IshaIqama = row.IshaIqama is { } ii ? FormatTime(ii) : null,
        };

        /// <summary>Returns the domain row, or <see langword="null"/> when the stored values are unusable.</summary>
        public CsvPrayerTimesRow? ToDomain()
        {
            if (ParseDate(Day) is not { } date ||
                ParseTime(FajrAthan) is not { } fajr ||
                ParseTime(Sunrise) is not { } sunrise ||
                ParseTime(DhuhrAthan) is not { } dhuhr ||
                ParseTime(AsrAthan) is not { } asr ||
                ParseTime(MaghribAthan) is not { } maghrib ||
                ParseTime(IshaAthan) is not { } isha)
            {
                return null;
            }

            return new CsvPrayerTimesRow(
                date, fajr, sunrise, dhuhr, asr, maghrib, isha,
                FajrIqama: ParseTime(FajrIqama),
                DhuhrIqama: ParseTime(DhuhrIqama),
                AsrIqama: ParseTime(AsrIqama),
                MaghribIqama: ParseTime(MaghribIqama),
                IshaIqama: ParseTime(IshaIqama));
        }
    }

    /// <summary>Single-row table holding the feed URL, validator, and check timestamps.</summary>
    [Table("csv_prayer_times_feed")]
    private sealed class FeedRow
    {
        public const int SingletonId = 1;

        [PrimaryKey]
        public int Id { get; set; } = SingletonId;

        public string FeedUrl { get; set; } = string.Empty;

        public string? ETag { get; set; }

        /// <summary>UTC ticks of the last change check, or 0 when never checked.</summary>
        public long LastCheckedAtTicks { get; set; }

        /// <summary>UTC ticks of the last successful row replacement, or 0 when never stored.</summary>
        public long LastUpdatedAtTicks { get; set; }
    }
}
