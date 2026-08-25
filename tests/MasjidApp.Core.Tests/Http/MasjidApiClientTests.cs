using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using MasjidApp.Core.Caching;
using MasjidApp.Core.Http;
using Polly;
using Polly.Timeout;

namespace MasjidApp.Core.Tests.Http;

// QA-02, QA-06: unit tests for the API client's retry/backoff, 304, tolerant parsing,
// and cache-fallback behaviour.

/// <summary>Model used in API client tests.</summary>
public sealed record TestDto(string Name, int Value);

[JsonSerializable(typeof(TestDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
internal sealed partial class TestDtoContext : JsonSerializerContext { }

public sealed class MasjidApiClientTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteCacheStore _store;
    private static readonly JsonTypeInfo<TestDto> TypeInfo =
        TestDtoContext.Default.TestDto;

    public MasjidApiClientTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        _store = new SqliteCacheStore(_dbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static MasjidApiClient BuildClient(
        HttpMessageHandler handler,
        ICacheStore? store = null,
        ResiliencePipeline<HttpResponseMessage>? pipeline = null,
        TimeProvider? timeProvider = null)
    {
        var http = new HttpClient(handler);
        return new MasjidApiClient(http, store ?? new NullCacheStore(), pipeline, timeProvider);
    }

    private static ResiliencePipeline<HttpResponseMessage> NoRetryPipeline() =>
        new ResiliencePipelineBuilder<HttpResponseMessage>().Build();

    // ── 200 OK - fresh response ────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_OkResponse_ReturnsFresh()
    {
        var json = """{"name":"Test","value":42}""";
        var handler = new FakeHttpHandler(HttpStatusCode.OK, json);
        var client = BuildClient(handler, _store, NoRetryPipeline());

        var result = await client.GetAsync("http://test.local/test", TypeInfo);

        result.Should().BeOfType<ApiResult<TestDto>.Fresh>();
        var fresh = (ApiResult<TestDto>.Fresh)result;
        fresh.Value.Name.Should().Be("Test");
        fresh.Value.Value.Should().Be(42);
        fresh.FetchedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetStatusCodeAsync_OkWebsite_ReturnsOk()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "website");
        var client = BuildClient(handler, _store, NoRetryPipeline());

        var result = await client.GetStatusCodeAsync("https://example.org/");

        result.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsFailedWithStatusCode()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, string.Empty);
        var client = BuildClient(handler, _store, NoRetryPipeline());

        var result = await client.GetAsync(
            "https://example.org/wp-json/masjid/v1/config",
            TypeInfo,
            ApiCachePolicy.ForceRefresh);

        result.Should().BeOfType<ApiResult<TestDto>.Failed>()
            .Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── 200 OK - response is persisted to cache ────────────────────────────────

    [Fact]
    public async Task GetAsync_OkResponse_PersistsToCacheStore()
    {
        var json = """{"name":"Cached","value":7}""";
        var handler = new FakeHttpHandler(HttpStatusCode.OK, json);
        var client = BuildClient(handler, _store, NoRetryPipeline());

        await client.GetAsync("http://test.local/cache-test", TypeInfo);

        var cached = await _store.GetAsync("http://test.local/cache-test");
        cached.Should().NotBeNull();
        cached!.Body.Should().Be(json);
    }

    [Fact]
    public async Task GetAsync_CachedValue_ReturnsBeforeBackgroundRequestCompletes()
    {
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var seedEntry = new CacheEntry(
            "http://test.local/fresh-cache",
            """{"name":"Cached","value":7}""",
            null,
            null,
            now.AddHours(-23));
        await _store.SetAsync(seedEntry);

        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpHandler(async (_, _) =>
        {
            requestStarted.TrySetResult();
            await releaseRequest.Task;
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        var client = BuildClient(handler, _store, NoRetryPipeline(), new TestTimeProvider(now));

        var result = await client.GetAsync("http://test.local/fresh-cache", TypeInfo);

        result.Should().BeOfType<ApiResult<TestDto>.Cached>()
            .Which.Value.Name.Should().Be("Cached");
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        releaseRequest.TrySetResult();
    }

    [Fact]
    public async Task GetAsync_OldCache_ReturnsCachedAndUpdatesItInBackground()
    {
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var seedEntry = new CacheEntry(
            "http://test.local/expired-cache",
            """{"name":"Cached","value":7}""",
            null,
            null,
            now.AddDays(-30));
        await _store.SetAsync(seedEntry);

        var handler = new FakeHttpHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"name":"Server","value":8}"""),
            });
        });
        var trackingStore = new TrackingCacheStore(_store);
        var client = BuildClient(handler, trackingStore, NoRetryPipeline(), new TestTimeProvider(now));

        var result = await client.GetAsync("http://test.local/expired-cache", TypeInfo);

        result.Should().BeOfType<ApiResult<TestDto>.Cached>()
            .Which.Value.Name.Should().Be("Cached");
        var refreshed = await trackingStore.NextSet.Task.WaitAsync(TimeSpan.FromSeconds(1));
        refreshed.Body.Should().Contain("Server");
    }

    [Fact]
    public async Task GetAsync_ConcurrentCachedReads_DeduplicateBackgroundRefresh()
    {
        const string Url = "http://test.local/deduplicated";
        await _store.SetAsync(new CacheEntry(
            Url,
            """{"name":"Cached","value":7}""",
            "\"v1\"",
            null,
            DateTimeOffset.UtcNow));

        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var handler = new FakeHttpHandler(async (_, _) =>
        {
            Interlocked.Increment(ref callCount);
            requestStarted.TrySetResult();
            await releaseRequest.Task;
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        var trackingStore = new TrackingCacheStore(_store);
        var client = BuildClient(handler, trackingStore, NoRetryPipeline());

        var first = await client.GetAsync(Url, TypeInfo);
        var second = await client.GetAsync(Url, TypeInfo);

        first.Should().BeOfType<ApiResult<TestDto>.Cached>();
        second.Should().BeOfType<ApiResult<TestDto>.Cached>();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        callCount.Should().Be(1);
        releaseRequest.TrySetResult();
        await trackingStore.NextSet.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetAsync_CacheOnlyWithCachedValue_ReturnsWithoutHttpRequest()
    {
        const string Url = "http://test.local/cache-only-hit";
        await _store.SetAsync(new CacheEntry(
            Url,
            """{"name":"Cached","value":7}""",
            null,
            null,
            DateTimeOffset.UtcNow));
        var requestCount = 0;
        var handler = new FakeHttpHandler((_, _) =>
        {
            Interlocked.Increment(ref requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        var client = BuildClient(handler, _store, NoRetryPipeline());

        var result = await client.GetAsync(Url, TypeInfo, ApiCachePolicy.CacheOnly);

        result.Should().BeOfType<ApiResult<TestDto>.Cached>()
            .Which.Value.Name.Should().Be("Cached");
        requestCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_CacheOnlyWithoutCachedValue_FailsWithoutHttpRequest()
    {
        var requestCount = 0;
        var handler = new FakeHttpHandler((_, _) =>
        {
            Interlocked.Increment(ref requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var client = BuildClient(handler, _store, NoRetryPipeline());

        var result = await client.GetAsync(
            "http://test.local/cache-only-miss",
            TypeInfo,
            ApiCachePolicy.CacheOnly);

        result.Should().BeOfType<ApiResult<TestDto>.Failed>();
        requestCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_ForceRefresh_ContactsServerWhenCacheIsFresh()
    {
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var seedEntry = new CacheEntry(
            "http://test.local/forced-cache",
            """{"name":"Cached","value":7}""",
            null,
            null,
            now);
        await _store.SetAsync(seedEntry);

        var handler = new FakeHttpHandler(HttpStatusCode.OK, """{"name":"Server","value":8}""");
        var client = BuildClient(handler, _store, NoRetryPipeline(), new TestTimeProvider(now));

        var result = await client.GetAsync(
            "http://test.local/forced-cache",
            TypeInfo,
            cachePolicy: ApiCachePolicy.ForceRefresh);

        result.Should().BeOfType<ApiResult<TestDto>.Fresh>()
            .Which.Value.Name.Should().Be("Server");
    }

    [Fact]
    public async Task GetAsync_MalformedFreshCache_ContactsServer()
    {
        var now = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var seedEntry = new CacheEntry(
            "http://test.local/malformed-cache",
            "not-json",
            null,
            null,
            now);
        await _store.SetAsync(seedEntry);

        var handler = new FakeHttpHandler(HttpStatusCode.OK, """{"name":"Server","value":8}""");
        var client = BuildClient(handler, _store, NoRetryPipeline(), new TestTimeProvider(now));

        var result = await client.GetAsync("http://test.local/malformed-cache", TypeInfo);

        result.Should().BeOfType<ApiResult<TestDto>.Fresh>()
            .Which.Value.Name.Should().Be("Server");
    }

    // ── 304 Not Modified ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_NotModified_ReturnsNotModified_WithCachedValue()
    {
        // Pre-seed the cache.
        var cachedJson = """{"name":"FromCache","value":99}""";
        var seedEntry = new CacheEntry("http://test.local/notmod", cachedJson, "\"etag-abc\"", null, DateTimeOffset.UtcNow.AddMinutes(-5));
        await _store.SetAsync(seedEntry);

        var handler = new FakeHttpHandler(HttpStatusCode.NotModified, string.Empty);
        var client = BuildClient(handler, _store, NoRetryPipeline());

        var result = await client.GetAsync(
            "http://test.local/notmod",
            TypeInfo,
            ApiCachePolicy.ForceRefresh);

        result.Should().BeOfType<ApiResult<TestDto>.NotModified>();
        var nm = (ApiResult<TestDto>.NotModified)result;
        nm.Value.Name.Should().Be("FromCache");
        nm.Value.Value.Should().Be(99);
    }

    [Fact]
    public async Task GetAsync_CachedEtag_SendsIfNoneMatchHeader()
    {
        var cachedJson = """{"name":"X","value":1}""";
        var seedEntry = new CacheEntry("http://test.local/etag-test", cachedJson, "\"my-etag\"", null, DateTimeOffset.UtcNow);
        await _store.SetAsync(seedEntry);

        string? capturedEtag = null;
        var handler = new FakeHttpHandler((req, _) =>
        {
            capturedEtag = req.Headers.IfNoneMatch.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"name":"X","value":1}"""),
            });
        });
        var client = BuildClient(handler, _store, NoRetryPipeline());

        await client.GetAsync(
            "http://test.local/etag-test",
            TypeInfo,
            ApiCachePolicy.ForceRefresh);

        capturedEtag.Should().Contain("my-etag");
    }

    [Fact]
    public async Task GetAsync_WeakEtagFromServer_StoredAndEchoedVerbatim()
    {
        // The masjid REST API emits weak validators (W/"<sha256>"); dropping the W/
        // prefix would leave the client relying on the server normalizing it back.
        const string WeakEtag = "W/\"9f2a\"";
        var handler = new FakeHttpHandler((req, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"name":"X","value":1}"""),
            };
            response.Headers.TryAddWithoutValidation("ETag", WeakEtag);
            return Task.FromResult(response);
        });
        var client = BuildClient(handler, _store, NoRetryPipeline());

        await client.GetAsync("http://test.local/weak-etag", TypeInfo, ApiCachePolicy.ForceRefresh);

        (await _store.GetAsync("http://test.local/weak-etag"))!.ETag.Should().Be(WeakEtag);

        string? capturedEtag = null;
        var conditionalHandler = new FakeHttpHandler((req, _) =>
        {
            capturedEtag = req.Headers.TryGetValues("If-None-Match", out var values)
                ? string.Join(", ", values)
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });
        var conditionalClient = BuildClient(conditionalHandler, _store, NoRetryPipeline());

        var result = await conditionalClient.GetAsync(
            "http://test.local/weak-etag", TypeInfo, ApiCachePolicy.ForceRefresh);

        capturedEtag.Should().Be(WeakEtag);
        result.Should().BeOfType<ApiResult<TestDto>.NotModified>();
    }

    // ── Network failure → fall back to cache ──────────────────────────────────

    [Fact]
    public async Task GetAsync_NetworkFailure_ReturnsStale_WhenCacheAvailable()
    {
        var cachedJson = """{"name":"Stale","value":0}""";
        var seedEntry = new CacheEntry("http://test.local/stale-test", cachedJson, null, null, DateTimeOffset.UtcNow.AddHours(-2));
        await _store.SetAsync(seedEntry);

        var handler = new ThrowingHttpHandler(new HttpRequestException("Connection refused"));
        var client = BuildClient(handler, _store, NoRetryPipeline());

        var result = await client.GetAsync(
            "http://test.local/stale-test",
            TypeInfo,
            ApiCachePolicy.ForceRefresh);

        result.Should().BeOfType<ApiResult<TestDto>.Stale>();
        var stale = (ApiResult<TestDto>.Stale)result;
        stale.Value.Name.Should().Be("Stale");
        stale.Cause.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task GetAsync_NetworkFailure_ReturnsFailed_WhenNoCacheAvailable()
    {
        var handler = new ThrowingHttpHandler(new HttpRequestException("No network"));
        var client = BuildClient(handler, null, NoRetryPipeline());

        var result = await client.GetAsync("http://test.local/no-cache", TypeInfo);

        result.Should().BeOfType<ApiResult<TestDto>.Failed>();
    }

    // ── Malformed JSON - tolerant parsing ─────────────────────────────────────

    [Fact]
    public async Task GetAsync_MalformedJson_ReturnsFailed_DoesNotThrow()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "this is not json {{{{");
        var client = BuildClient(handler, null, NoRetryPipeline());

        var act = () => client.GetAsync("http://test.local/bad-json", TypeInfo);
        await act.Should().NotThrowAsync("API-07: malformed payloads must never throw");

        var result = await client.GetAsync("http://test.local/bad-json2", TypeInfo);
        result.Should().BeOfType<ApiResult<TestDto>.Failed>()
            .Which.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAsync_NoContent_ReturnsFailedWithNonOkStatusCode()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NoContent, string.Empty);
        var client = BuildClient(handler, null, NoRetryPipeline());

        var result = await client.GetAsync("https://example.org/config", TypeInfo);

        result.Should().BeOfType<ApiResult<TestDto>.Failed>()
            .Which.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetAsync_MalformedJson_ReturnsStale_WhenCacheAvailable()
    {
        var cachedJson = """{"name":"Good","value":5}""";
        var seedEntry = new CacheEntry("http://test.local/bad-json-cached", cachedJson, null, null, DateTimeOffset.UtcNow.AddMinutes(-10));
        await _store.SetAsync(seedEntry);

        var handler = new FakeHttpHandler(HttpStatusCode.OK, "NOT JSON");
        var client = BuildClient(handler, _store, NoRetryPipeline());

        var result = await client.GetAsync(
            "http://test.local/bad-json-cached",
            TypeInfo,
            ApiCachePolicy.ForceRefresh);

        result.Should().BeOfType<ApiResult<TestDto>.Stale>()
            .Which.Value.Name.Should().Be("Good");
    }

    // ── Retry / backoff ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_TransientServerError_RetriesAndEventuallySucceeds()
    {
        var json = """{"name":"Retry","value":3}""";
        // First two calls return 503, third returns 200.
        var callCount = 0;
        var handler = new FakeHttpHandler((_, ct) =>
        {
            callCount++;
            var status = callCount < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            var content = callCount < 3 ? string.Empty : json;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(content),
            });
        });

        // Build a minimal pipeline: 3 retries, no delay (for speed).
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(static r => (int)r.StatusCode >= 500),
            })
            .Build();

        var client = BuildClient(handler, null, pipeline);
        var result = await client.GetAsync("http://test.local/retry-test", TypeInfo);

        result.Should().BeOfType<ApiResult<TestDto>.Fresh>();
        callCount.Should().Be(3, "two 503s then a 200");
    }

    [Fact]
    public async Task GetAsync_AllRetriesExhausted_FallsBackToCache()
    {
        var cachedJson = """{"name":"CachedRetry","value":1}""";
        var seedEntry = new CacheEntry("http://test.local/retry-cache", cachedJson, null, null, DateTimeOffset.UtcNow.AddHours(-1));
        await _store.SetAsync(seedEntry);

        var handler = new FakeHttpHandler(HttpStatusCode.ServiceUnavailable, string.Empty);

        // 1 retry, no delay.
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(static r => (int)r.StatusCode >= 500),
            })
            .Build();

        var client = BuildClient(handler, _store, pipeline);
        var result = await client.GetAsync(
            "http://test.local/retry-cache",
            TypeInfo,
            ApiCachePolicy.ForceRefresh);

        result.Should().BeOfType<ApiResult<TestDto>.Stale>()
            .Which.Value.Name.Should().Be("CachedRetry");
    }

    // ── Timeout ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_Timeout_ReturnsFailed_WhenNoCacheAvailable()
    {
        // Pipeline with a 50 ms per-attempt timeout and no retry.
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(TimeSpan.FromMilliseconds(50))
            .Build();

        // Handler that delays longer than the timeout.
        var handler = new FakeHttpHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = BuildClient(handler, null, pipeline);
        var result = await client.GetAsync("http://test.local/timeout-test", TypeInfo);

        result.Should().BeOfType<ApiResult<TestDto>.Failed>();
    }

    [Fact]
    public async Task GetImageAsync_CachedImage_ReturnsCachedAndSeparatesTenants()
    {
        var imageBytes = new byte[] { 137, 80, 78, 71 };
        var handler = new FakeHttpHandler((_, _) =>
        {
            var content = new ByteArrayContent(imageBytes);
            content.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var client = BuildClient(handler, _store, NoRetryPipeline());

        var fresh = await client.GetImageAsync(
            "https://example.org/images/splash.png",
            "masjid-a",
            ApiCachePolicy.ForceRefresh);
        var cached = await client.GetImageAsync(
            "https://example.org/images/splash.png",
            "masjid-a",
            ApiCachePolicy.CacheOnly);
        var otherTenant = await client.GetImageAsync(
            "https://example.org/images/splash.png",
            "masjid-b",
            ApiCachePolicy.CacheOnly);

        fresh.Should().BeOfType<ApiResult<CachedImage>.Fresh>()
            .Which.Value.Bytes.Should().Equal(imageBytes);
        cached.Should().BeOfType<ApiResult<CachedImage>.Cached>()
            .Which.Value.Bytes.Should().Equal(imageBytes);
        otherTenant.Should().BeOfType<ApiResult<CachedImage>.Failed>();
    }

    [Fact]
    public async Task GetImageAsync_NetworkFailure_ReturnsStaleCachedImage()
    {
        var imageBytes = new byte[] { 137, 80, 78, 71 };
        var successfulHandler = new FakeHttpHandler((_, _) =>
        {
            var content = new ByteArrayContent(imageBytes);
            content.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var successfulClient = BuildClient(successfulHandler, _store, NoRetryPipeline());
        await successfulClient.GetImageAsync(
            "https://example.org/images/splash.png",
            "masjid-a",
            ApiCachePolicy.ForceRefresh);

        var failedClient = BuildClient(
            new ThrowingHttpHandler(new HttpRequestException("offline")),
            _store,
            NoRetryPipeline());
        var result = await failedClient.GetImageAsync(
            "https://example.org/images/splash.png",
            "masjid-a",
            ApiCachePolicy.ForceRefresh);

        result.Should().BeOfType<ApiResult<CachedImage>.Stale>()
            .Which.Value.Bytes.Should().Equal(imageBytes);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private sealed class NullCacheStore : ICacheStore
    {
        public Task<CacheEntry?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<CacheEntry?>(null);

        public Task SetAsync(CacheEntry entry, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class TrackingCacheStore(ICacheStore inner) : ICacheStore
    {
        public TaskCompletionSource<CacheEntry> NextSet { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CacheEntry?> GetAsync(string key, CancellationToken ct = default) =>
            inner.GetAsync(key, ct);

        public async Task SetAsync(CacheEntry entry, CancellationToken ct = default)
        {
            await inner.SetAsync(entry, ct);
            NextSet.TrySetResult(entry);
        }
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public FakeHttpHandler(HttpStatusCode status, string body)
            : this((_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
            }))
        { }

        public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHttpHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(_exception);
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
