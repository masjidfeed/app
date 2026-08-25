namespace MasjidApp.Core.Http;

// API-08, NFR-OF-03: metadata persisted alongside every cached response body.

/// <summary>
/// Immutable model persisted to the SQLite cache store for a single API endpoint.
/// Carries the raw JSON body and HTTP validation headers so the client can issue
/// conditional requests (<c>If-None-Match</c> / <c>If-Modified-Since</c>).
/// </summary>
/// <param name="Key">Cache key - typically the relative request path.</param>
/// <param name="Body">Raw JSON response body, preserved verbatim for tolerant re-parsing.</param>
/// <param name="ETag"><c>ETag</c> header value (including quotes), or <c>null</c> if absent.</param>
/// <param name="LastModified"><c>Last-Modified</c> header value in RFC 1123 format, or <c>null</c>.</param>
/// <param name="FetchedAt">UTC time at which the body was last fetched from the server.</param>
public sealed record CacheEntry(
    string Key,
    string Body,
    string? ETag,
    string? LastModified,
    DateTimeOffset FetchedAt);

