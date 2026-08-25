using MasjidApp.Core.Http;
using SQLite;

namespace MasjidApp.Core.Caching;

// API-08, NFR-OF-03: SQLite-backed implementation of ICacheStore.

/// <summary>
/// SQLite-backed implementation of <see cref="ICacheStore"/>.
/// Uses <c>sqlite-net-pcl</c> for cross-platform async SQLite access.
/// The database is created / migrated lazily on first use.
/// </summary>
public sealed class SqliteCacheStore : ICacheStore, ICacheStoreLifecycle, IDisposable
{
    private readonly string _databasePath;
    private SQLiteAsyncConnection? _db;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    /// <summary>
    /// Initialises the store.  The database file is not opened until the first operation.
    /// </summary>
    /// <param name="databasePath">Full file-system path to the SQLite database file.</param>
    public SqliteCacheStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    /// <inheritdoc/>
    public async Task<CacheEntry?> GetAsync(string key, CancellationToken ct = default)
    {
        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = await GetDatabaseAsync().ConfigureAwait(false);
            var row = await db.Table<CacheRow>()
                .Where(r => r.Key == key)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
            return row is null ? null : row.ToCacheEntry();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync(CacheEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = await GetDatabaseAsync().ConfigureAwait(false);
            await db.InsertOrReplaceAsync(CacheRow.FromCacheEntry(entry)).ConfigureAwait(false);
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
            await connection.CreateTableAsync<CacheRow>().ConfigureAwait(false);
            _db = connection;
        }

        return _db;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _operationLock.Dispose();
    }

    // ── Table model ─────────────────────────────────────────────────────────────

    [Table("cache_entries")]
    private sealed class CacheRow
    {
        [PrimaryKey]
        public string Key { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public string? ETag { get; set; }

        public string? LastModified { get; set; }

        /// <summary>UTC ticks for <see cref="CacheEntry.FetchedAt"/>.</summary>
        public long FetchedAtTicks { get; set; }

        public CacheEntry ToCacheEntry() =>
            new(Key, Body, ETag, LastModified, new DateTimeOffset(FetchedAtTicks, TimeSpan.Zero));

        public static CacheRow FromCacheEntry(CacheEntry e) => new()
        {
            Key = e.Key,
            Body = e.Body,
            ETag = e.ETag,
            LastModified = e.LastModified,
            FetchedAtTicks = e.FetchedAt.UtcTicks,
        };
    }
}
