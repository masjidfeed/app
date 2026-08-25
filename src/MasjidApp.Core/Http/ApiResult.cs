using System.Net;

namespace MasjidApp.Core.Http;

// API-09, NFR-RL-01: typed result that distinguishes data freshness from failure
// so the UI can show a non-blocking error/retry state without crashing.

/// <summary>
/// Discriminated union returned by <see cref="IMasjidApiClient.GetAsync{T}"/>.
/// Every case carries the data that is available, enabling the UI to always render
/// something (even if stale) while surfacing connectivity state non-disruptively.
/// </summary>
/// <typeparam name="T">The deserialized response type.</typeparam>
public abstract record ApiResult<T> where T : class
{
    private ApiResult() { }

    /// <summary>
    /// A successful response with freshly fetched data (<c>200 OK</c>).
    /// </summary>
    public sealed record Fresh(T Value, DateTimeOffset FetchedAt) : ApiResult<T>;

    /// <summary>
    /// A locally cached response that is still within the freshness window.
    /// </summary>
    public sealed record Cached(T Value, DateTimeOffset FetchedAt) : ApiResult<T>;

    /// <summary>
    /// Server replied <c>304 Not Modified</c>; the cached body is still valid.
    /// </summary>
    public sealed record NotModified(T Value, DateTimeOffset FetchedAt) : ApiResult<T>;

    /// <summary>
    /// Network or server error; cache is available but may be stale.
    /// </summary>
    public sealed record Stale(T Value, DateTimeOffset FetchedAt, Exception? Cause) : ApiResult<T>;

    /// <summary>
    /// Irrecoverable failure (no cache available, or deserialization failed on cached data).
    /// </summary>
    public sealed record Failed(Exception? Cause, string Message) : ApiResult<T>
    {
        /// <summary>The server status when a response was received; otherwise <c>null</c>.</summary>
        public HttpStatusCode? StatusCode { get; init; }
    }

    /// <summary>
    /// Returns the data value when the result is <see cref="Fresh"/>,
    /// <see cref="Cached"/>, <see cref="NotModified"/>, or <see cref="Stale"/>;
    /// otherwise <c>null</c>.
    /// </summary>
    public T? ValueOrDefault => this switch
    {
        Fresh f => f.Value,
        Cached c => c.Value,
        NotModified nm => nm.Value,
        Stale s => s.Value,
        _ => null,
    };

    /// <summary>
    /// <c>true</c> when data is available (not <see cref="Failed"/>).
    /// </summary>
    public bool HasValue => ValueOrDefault is not null;

    /// <summary>
    /// The timestamp associated with this result, if data is available.
    /// </summary>
    public DateTimeOffset? FetchedAtOrNull => this switch
    {
        Fresh f => f.FetchedAt,
        Cached c => c.FetchedAt,
        NotModified nm => nm.FetchedAt,
        Stale s => s.FetchedAt,
        _ => null,
    };
}
