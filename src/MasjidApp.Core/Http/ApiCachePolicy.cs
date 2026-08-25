namespace MasjidApp.Core.Http;

/// <summary>Controls whether an API request returns cached content or waits for the server.</summary>
public enum ApiCachePolicy
{
    /// <summary>Return any valid cached payload immediately and revalidate it in the background.</summary>
    PreferCache,

    /// <summary>Return a cached payload without contacting the server; fail when none is available.</summary>
    CacheOnly,

    /// <summary>Bypass the freshness window and contact the server.</summary>
    ForceRefresh,
}
