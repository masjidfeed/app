using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MasjidApp.Core.Caching;
using MasjidApp.Core.Serialization;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace MasjidApp.Core.Http;

// API-07, API-08, API-09, API-11, TC-06, NFR-RL-01

/// <summary>
/// Default implementation of <see cref="IMasjidApiClient"/>.
/// Wraps every outbound request in an exponential-backoff retry + per-attempt timeout
/// pipeline, issues conditional requests (ETag / Last-Modified), and falls back to the
/// local SQLite cache on any network or server failure.
/// </summary>
public sealed class MasjidApiClient : IMasjidApiClient
{
    private const long MaximumCachedImageBytes = 5 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly ICacheStore _cache;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private readonly TimeProvider _timeProvider;
    private readonly object _backgroundRefreshLock = new();
    private readonly HashSet<string> _automaticallyRefreshedUrls = [];

    /// <summary>
    /// Creates a client with an explicit resilience pipeline (used in tests).
    /// Pass <c>null</c> for <paramref name="pipeline"/> to use the built-in default:
    /// up to 3 retries with exponential backoff + jitter, 10-second per-attempt timeout.
    /// All URLs passed to <see cref="GetAsync{T}"/> must be absolute -
    /// the base URL comes from the active masjid's stored entry or its config document,
    /// not from environment configuration.
    /// </summary>
    public MasjidApiClient(
        HttpClient http,
        ICacheStore cache,
        ResiliencePipeline<HttpResponseMessage>? pipeline = null,
        TimeProvider? timeProvider = null)
    {
        _http = http;
        _cache = cache;
        _pipeline = pipeline ?? BuildDefaultPipeline();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<ApiResult<T>> GetAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(typeInfo);

        // 1. Load the cached entry (used for conditional-request headers + fallback).
        var cached = await _cache.GetAsync(path, ct).ConfigureAwait(false);

        if (cachePolicy is ApiCachePolicy.PreferCache or ApiCachePolicy.CacheOnly && cached is not null)
        {
            var cachedValue = TryDeserialize(cached.Body, typeInfo);
            if (cachedValue is not null)
            {
                if (cachePolicy == ApiCachePolicy.PreferCache)
                {
                    StartBackgroundRefresh(path, typeInfo, ct);
                }

                return new ApiResult<T>.Cached(cachedValue, cached.FetchedAt);
            }
        }

        if (cachePolicy == ApiCachePolicy.CacheOnly)
        {
            return new ApiResult<T>.Failed(null, "No valid cached response is available.");
        }

        // 2. Execute the HTTP call through the resilience pipeline.
        HttpResponseMessage? response = null;
        Exception? networkFailure = null;

        try
        {
            response = await _pipeline.ExecuteAsync(
                async (cancellationToken) =>
                {
                    // Build a fresh request on each attempt - HttpRequestMessage is
                    // single-use and cannot be resent after the first attempt.
                    using var request = BuildRequest(path, cached);
                    return await _http
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
        }
        catch (TimeoutRejectedException ex)
        {
            networkFailure = ex;
        }
        catch (HttpRequestException ex)
        {
            networkFailure = ex;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Inner cancellation (e.g. from per-attempt timeout token) - treat as failure.
            networkFailure = ex;
        }

        // 3. All retries exhausted / network unavailable → serve stale cache or fail.
        if (networkFailure is not null)
        {
            return cached is null
                ? new ApiResult<T>.Failed(networkFailure, networkFailure.Message)
                : TryReturnStale<T>(cached, typeInfo, networkFailure)
                  ?? (ApiResult<T>)new ApiResult<T>.Failed(networkFailure, networkFailure.Message);
        }

        // 4. Handle 304 Not Modified - refresh the timestamp, serve cached body.
        if (response!.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            response.Dispose();
            var parsed = TryDeserialize(cached.Body, typeInfo);
            if (parsed is not null)
            {
                var refreshed = cached with { FetchedAt = _timeProvider.GetUtcNow() };
                await _cache.SetAsync(refreshed, ct).ConfigureAwait(false);
                return new ApiResult<T>.NotModified(parsed, refreshed.FetchedAt);
            }
        }

        // 5. Non-success status codes → serve stale if available.
        if (!response!.IsSuccessStatusCode)
        {
                        var statusCode = response.StatusCode;
            response.Dispose();
            return cached is null
                                ? new ApiResult<T>.Failed(null, $"HTTP {(int)statusCode}") { StatusCode = statusCode }
                : TryReturnStale<T>(cached, typeInfo, cause: null)
                                    ?? (ApiResult<T>)new ApiResult<T>.Failed(null, $"HTTP {(int)statusCode}") { StatusCode = statusCode };
        }

        // 6. Read and parse the successful body.
        var responseStatusCode = response.StatusCode;
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }

        var value = TryDeserialize(body, typeInfo);
        if (value is null)
        {
            // Malformed body - degrade to cached if available (API-07, NFR-RL-01).
            return cached is null
                                ? new ApiResult<T>.Failed(null, "Response deserialization failed") { StatusCode = responseStatusCode }
                : TryReturnStale<T>(cached, typeInfo, cause: null)
                                    ?? (ApiResult<T>)new ApiResult<T>.Failed(null, "Response deserialization failed") { StatusCode = responseStatusCode };
        }

        // 7. Persist to cache and return fresh.
        var entry = new CacheEntry(
            path,
            body,
            // ToString() keeps the W/ prefix; ETag.Tag would silently strip it.
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified?.ToString("R"),
            _timeProvider.GetUtcNow());

        await _cache.SetAsync(entry, ct).ConfigureAwait(false);
        return new ApiResult<T>.Fresh(value, entry.FetchedAt);
    }

    /// <inheritdoc/>
    public async Task<ApiResult<CachedImage>> GetImageAsync(
        string imageUrl,
        string cacheKey,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);

        var imageCacheKey = $"image:{cacheKey}:{imageUrl}";
        var cached = await _cache.GetAsync(imageCacheKey, ct).ConfigureAwait(false);
        var cachedImage = TryDeserializeImage(cached?.Body);

        if (cachePolicy is ApiCachePolicy.PreferCache or ApiCachePolicy.CacheOnly && cachedImage is not null)
        {
            if (cachePolicy == ApiCachePolicy.PreferCache)
            {
                StartBackgroundImageRefresh(imageUrl, cacheKey, imageCacheKey, ct);
            }

            return new ApiResult<CachedImage>.Cached(cachedImage, cached!.FetchedAt);
        }

        if (cachePolicy == ApiCachePolicy.CacheOnly)
        {
            return new ApiResult<CachedImage>.Failed(null, "No valid cached image is available.");
        }

        HttpResponseMessage? response = null;
        Exception? networkFailure = null;
        try
        {
            response = await _pipeline.ExecuteAsync(
                async cancellationToken =>
                {
                    using var request = BuildRequest(imageUrl, cached);
                    return await _http
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
        }
        catch (TimeoutRejectedException ex)
        {
            networkFailure = ex;
        }
        catch (HttpRequestException ex)
        {
            networkFailure = ex;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            networkFailure = ex;
        }

        if (networkFailure is not null)
        {
            return ReturnCachedImageOrFailure(cached, cachedImage, networkFailure, networkFailure.Message);
        }

        if (response!.StatusCode == HttpStatusCode.NotModified && cachedImage is not null && cached is not null)
        {
            response.Dispose();
            var refreshed = cached with { FetchedAt = _timeProvider.GetUtcNow() };
            await _cache.SetAsync(refreshed, ct).ConfigureAwait(false);
            return new ApiResult<CachedImage>.NotModified(cachedImage, refreshed.FetchedAt);
        }

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = response.StatusCode;
            response.Dispose();
            return ReturnCachedImageOrFailure(cached, cachedImage, null, $"HTTP {(int)statusCode}", statusCode);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (response.Content.Headers.ContentLength is > MaximumCachedImageBytes ||
            string.IsNullOrWhiteSpace(mediaType) ||
            !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            response.Dispose();
            return ReturnCachedImageOrFailure(cached, cachedImage, null, "Response is not a supported image.");
        }

        byte[] bytes;
        try
        {
            bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }

        if (bytes.Length == 0 || bytes.Length > MaximumCachedImageBytes)
        {
            return ReturnCachedImageOrFailure(cached, cachedImage, null, "Image exceeds the supported size.");
        }

        var image = new CachedImage(bytes, mediaType);
        var entry = new CacheEntry(
            imageCacheKey,
            JsonSerializer.Serialize(image, MasjidCoreJsonContext.Default.CachedImage),
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified?.ToString("R"),
            _timeProvider.GetUtcNow());

        await _cache.SetAsync(entry, ct).ConfigureAwait(false);
        return new ApiResult<CachedImage>.Fresh(image, entry.FetchedAt);
    }

    /// <inheritdoc/>
    public async Task<HttpStatusCode?> GetStatusCodeAsync(
        string url,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        try
        {
            using var response = await _pipeline.ExecuteAsync(
                async cancellationToken =>
                {
                    using var request = BuildRequest(url, cached: null);
                    return await _http
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                },
                ct).ConfigureAwait(false);
            return response.StatusCode;
        }
        catch (TimeoutRejectedException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private void StartBackgroundRefresh<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
        where T : class
    {
        lock (_backgroundRefreshLock)
        {
            if (!_automaticallyRefreshedUrls.Add(path))
            {
                return;
            }
        }

        _ = RefreshInBackgroundAsync(path, typeInfo, ct);
    }

    private void StartBackgroundImageRefresh(
        string imageUrl,
        string cacheKey,
        string imageCacheKey,
        CancellationToken ct)
    {
        lock (_backgroundRefreshLock)
        {
            if (!_automaticallyRefreshedUrls.Add(imageCacheKey))
            {
                return;
            }
        }

        _ = RefreshImageInBackgroundAsync(imageUrl, cacheKey, ct);
    }

    private async Task RefreshInBackgroundAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
        where T : class
    {
        try
        {
            await GetAsync(path, typeInfo, ApiCachePolicy.ForceRefresh, ct)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Background refresh must never surface an unobserved exception.
        catch (Exception)
        {
            // GetAsync encodes expected transport and parse failures; this guards unexpected ones.
        }
#pragma warning restore CA1031
    }

    private async Task RefreshImageInBackgroundAsync(
        string imageUrl,
        string cacheKey,
        CancellationToken ct)
    {
        try
        {
            await GetImageAsync(imageUrl, cacheKey, ApiCachePolicy.ForceRefresh, ct)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Background refresh must never surface an unobserved exception.
        catch (Exception)
        {
            // GetImageAsync encodes expected transport failures; this guards unexpected ones.
        }
#pragma warning restore CA1031
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static HttpRequestMessage BuildRequest(string path, CacheEntry? cached)
    {
        // API-10: all request paths must be absolute URLs supplied by the active masjid's
        // stored entry (ConfigUrl) or its config document (EndpointsConfig fields).
        if (!path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Request path must be an absolute URL. Got: {path}", nameof(path));
        }

        var url = path;

        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (cached?.ETag is { Length: > 0 } etag)
        {
            // Echoed unvalidated so weak and non-conforming validators survive the round trip.
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        if (cached?.LastModified is { Length: > 0 } lm &&
            DateTimeOffset.TryParse(lm, out var lmDate))
        {
            request.Headers.IfModifiedSince = lmDate;
        }

        return request;
    }

    private static ApiResult<T>? TryReturnStale<T>(
        CacheEntry cached,
        JsonTypeInfo<T> typeInfo,
        Exception? cause)
        where T : class
    {
        var value = TryDeserialize(cached.Body, typeInfo);
        return value is null
            ? null
            : new ApiResult<T>.Stale(value, cached.FetchedAt, cause);
    }

    private static CachedImage? TryDeserializeImage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var image = JsonSerializer.Deserialize(body, MasjidCoreJsonContext.Default.CachedImage);
            return image is { Bytes.Length: > 0 } ? image : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ApiResult<CachedImage> ReturnCachedImageOrFailure(
        CacheEntry? cached,
        CachedImage? cachedImage,
        Exception? cause,
        string message,
        HttpStatusCode? statusCode = null) =>
        cachedImage is not null && cached is not null
            ? new ApiResult<CachedImage>.Stale(cachedImage, cached.FetchedAt, cause)
            : new ApiResult<CachedImage>.Failed(cause, message) { StatusCode = statusCode };

    private static TModel? TryDeserialize<TModel>(string? body, JsonTypeInfo<TModel> typeInfo)
        where TModel : class
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(body, typeInfo);
        }
        catch (JsonException)
        {
            // API-07: tolerate malformed payloads - degrade, never throw.
            return null;
        }
    }

    // ── Default resilience pipeline ────────────────────────────────────────────

    /// <summary>
    /// Builds the default pipeline:
    /// 3 retries with exponential backoff + jitter for 5xx / request-timeout failures,
    /// with a 10-second per-attempt timeout.  User-initiated cancellation is NOT retried.
    /// </summary>
    internal static ResiliencePipeline<HttpResponseMessage> BuildDefaultPipeline() =>
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            // Retry wraps the timeout: each attempt gets its own 10-second budget.
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(static r =>
                        (int)r.StatusCode >= 500 ||
                        r.StatusCode == HttpStatusCode.RequestTimeout),
            })
            // Per-attempt timeout (innermost) - fires TimeoutRejectedException.
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();
}
