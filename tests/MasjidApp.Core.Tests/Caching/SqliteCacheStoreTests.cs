using FluentAssertions;
using MasjidApp.Core.Caching;
using MasjidApp.Core.Http;

namespace MasjidApp.Core.Tests.Caching;

// QA-02: verifies that SqliteCacheStore persists entries across connection instances
// (simulating app restarts) and that CRUD operations behave correctly.

public sealed class SqliteCacheStoreTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteCacheStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cachetest_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private SqliteCacheStore CreateStore() => new(_dbPath);

    // ── Get on empty store ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.GetAsync("nonexistent");

        result.Should().BeNull();
    }

    // ── Set then Get ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsStoredEntry()
    {
        var store = CreateStore();
        var entry = new CacheEntry("/api/test", """{"key":"value"}""", "\"etag-1\"", "Thu, 01 Jan 2026 00:00:00 GMT", DateTimeOffset.UtcNow);

        await store.SetAsync(entry);
        var retrieved = await store.GetAsync("/api/test");

        retrieved.Should().NotBeNull();
        retrieved!.Key.Should().Be(entry.Key);
        retrieved.Body.Should().Be(entry.Body);
        retrieved.ETag.Should().Be(entry.ETag);
        retrieved.LastModified.Should().Be(entry.LastModified);
        retrieved.FetchedAt.Should().BeCloseTo(entry.FetchedAt, TimeSpan.FromMilliseconds(1));
    }

    // ── Replace existing entry ─────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_ExistingKey_ReplacesEntry()
    {
        var store = CreateStore();
        var original = new CacheEntry("/api/replace", "original", null, null, DateTimeOffset.UtcNow.AddMinutes(-10));
        var updated = new CacheEntry("/api/replace", "updated", "\"new-etag\"", null, DateTimeOffset.UtcNow);

        await store.SetAsync(original);
        await store.SetAsync(updated);
        var retrieved = await store.GetAsync("/api/replace");

        retrieved!.Body.Should().Be("updated");
        retrieved.ETag.Should().Be("\"new-etag\"");
    }

    // ── Multiple keys ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_MultipleKeys_EachReturnedIndependently()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;

        await store.SetAsync(new CacheEntry("/a", "body-a", null, null, now));
        await store.SetAsync(new CacheEntry("/b", "body-b", null, null, now));

        var a = await store.GetAsync("/a");
        var b = await store.GetAsync("/b");

        a!.Body.Should().Be("body-a");
        b!.Body.Should().Be("body-b");
    }

    // ── Persists across store instances (simulates app restart) ───────────────

    [Fact]
    public async Task SetAsync_PersistsAcrossInstances_SimulatingAppRestart()
    {
        // Act: write with first instance.
        var store1 = CreateStore();
        var entry = new CacheEntry("/restart-test", "persisted-body", "\"etag-x\"", null, DateTimeOffset.UtcNow);
        await store1.SetAsync(entry);

        // Act: read with a brand-new instance pointing to the same file.
        var store2 = CreateStore();
        var retrieved = await store2.GetAsync("/restart-test");

        // Assert: data survived the "restart".
        retrieved.Should().NotBeNull("NFR-OF-03: cached data must survive app restarts");
        retrieved!.Body.Should().Be("persisted-body");
        retrieved.ETag.Should().Be("\"etag-x\"");
    }

    // ── Application suspension ─────────────────────────────────────────────────

    [Fact]
    public async Task CloseAsync_AfterWrite_ReopensAndPreservesEntry()
    {
        var store = CreateStore();
        var entry = new CacheEntry(
            "/suspension-test",
            "persisted-body",
            "\"etag-suspend\"",
            null,
            DateTimeOffset.UtcNow);
        await store.SetAsync(entry);

        await store.CloseAsync();
        var retrieved = await store.GetAsync(entry.Key);

        retrieved.Should().NotBeNull();
        retrieved!.Body.Should().Be(entry.Body);
        retrieved.ETag.Should().Be(entry.ETag);
    }

    // ── Null ETag / LastModified roundtrip ─────────────────────────────────────

    [Fact]
    public async Task SetAsync_NullableFieldsNull_RoundtripsCorrectly()
    {
        var store = CreateStore();
        var entry = new CacheEntry("/null-fields", "body", null, null, DateTimeOffset.UtcNow);

        await store.SetAsync(entry);
        var retrieved = await store.GetAsync("/null-fields");

        retrieved!.ETag.Should().BeNull();
        retrieved.LastModified.Should().BeNull();
    }
}
