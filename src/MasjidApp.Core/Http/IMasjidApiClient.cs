using System.Net;
using System.Text.Json.Serialization.Metadata;

namespace MasjidApp.Core.Http;

// API-11: single typed API client seam; all feature batches call through this interface.

/// <summary>
/// Typed, resilient HTTP client for the Masjid API.
/// Enforces HTTPS/TLS 1.2+, request timeouts, exponential-backoff retry for transient
/// failures, conditional requests, and get-or-fetch fallback to the local cache.
/// </summary>
public interface IMasjidApiClient
{
    /// <summary>
    /// Fetches <typeparamref name="T"/> from <paramref name="path"/> (relative to the
    /// configured base URL), using the local cache for conditional requests and offline
    /// fallback.
    /// </summary>
    /// <typeparam name="T">The expected response type.  Must be a reference type with a
    /// corresponding <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.</typeparam>
    /// <param name="path">Relative API path, e.g. <c>/salahapi</c>.</param>
    /// <param name="typeInfo">
    /// Source-generated type metadata - pass the <c>JsonTypeInfo&lt;T&gt;</c> from the
    /// caller's <c>JsonSerializerContext</c>.  This keeps the client AOT/trimming-safe.
    /// </param>
    /// <param name="cachePolicy">Whether to return cached content immediately or wait for the server.</param>
    /// <param name="ct">Cancellation token for user-initiated cancellation.</param>
    /// <returns>
    /// An <see cref="ApiResult{T}"/> that is never <c>null</c> and never throws - all
    /// failure modes are encoded in the return type (<see cref="ApiResult{T}.Stale"/> or
    /// <see cref="ApiResult{T}.Failed"/>).
    /// </returns>
    Task<ApiResult<T>> GetAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
        where T : class;

    /// <summary>
    /// Fetches a remote image with conditional validation and offline fallback. The
    /// <paramref name="cacheKey"/> must identify the tenant that owns the image.
    /// </summary>
    Task<ApiResult<CachedImage>> GetImageAsync(
        string imageUrl,
        string cacheKey,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);

    /// <summary>
    /// Requests an absolute URL through the resilient transport pipeline and returns its
    /// final HTTP status, or <c>null</c> when no response could be obtained.
    /// </summary>
    Task<HttpStatusCode?> GetStatusCodeAsync(string url, CancellationToken ct = default);
}
