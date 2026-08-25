using MasjidApp.Core.Configuration;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;
using MasjidApp.Core.Serialization;
using MasjidApp.Core.Services;

namespace MasjidApp.Services;

// API-06, API-10, FR-ST-08: loads the active masjid's configuration document and
// exposes it to all feature batches.

/// <summary>
/// Fetches the active masjid's <c>/config</c> document via <see cref="IMasjidApiClient"/>
/// and exposes it through <see cref="IMasjidContext"/>.
/// </summary>
public sealed class MasjidContext : IMasjidContext
{
    private readonly IMasjidApiClient _api;
    private readonly IMasjidRegistry _registry;

    public MasjidContext(IMasjidApiClient api, IMasjidRegistry registry)
    {
        _api = api;
        _registry = registry;
    }

    /// <inheritdoc/>
    public MasjidStoredEntry? ActiveEntry => _registry.Active;

    /// <inheritdoc/>
    public event EventHandler ActiveMasjidChanged = static (_, _) => { };

    /// <inheritdoc/>
    public async Task<ApiResult<MasjidConfig>?> GetConfigAsync(
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        var active = _registry.Active;
        if (active is null)
        {
            return null;
        }

        // The config URL is a full absolute URL (per-masjid endpoint).
        var result = await _api
            .GetAsync(active.ConfigUrl, MasjidCoreJsonContext.Default.MasjidConfig, cachePolicy, ct)
            .ConfigureAwait(false);

        // API-10: resolve any relative endpoint URLs in the config against the masjid's
        // own config-document origin (not the environment base URL) before exposing them.
        return result switch
        {
            ApiResult<MasjidConfig>.Fresh f =>
                f with { Value = EndpointUrlResolver.Resolve(f.Value, active.ConfigUrl) },
            ApiResult<MasjidConfig>.Cached c =>
                c with { Value = EndpointUrlResolver.Resolve(c.Value, active.ConfigUrl) },
            ApiResult<MasjidConfig>.NotModified nm =>
                nm with { Value = EndpointUrlResolver.Resolve(nm.Value, active.ConfigUrl) },
            ApiResult<MasjidConfig>.Stale s =>
                s with { Value = EndpointUrlResolver.Resolve(s.Value, active.ConfigUrl) },
            _ => result,
        };
    }

    /// <inheritdoc/>
    public void SetActive(MasjidStoredEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _registry.Add(entry);
        _registry.SetActive(entry.Id);
        ActiveMasjidChanged.Invoke(this, EventArgs.Empty);
    }
}
