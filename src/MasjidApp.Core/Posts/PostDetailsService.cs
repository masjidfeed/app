using MasjidApp.Core.Configuration;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;
using MasjidApp.Core.Utils;

namespace MasjidApp.Core.Posts;

/// <summary>Fetches and normalizes one post through the resilient API client.</summary>
public sealed class PostDetailsService(IMasjidApiClient client) : IPostDetailsService
{
    public async Task<ApiResult<MasjidPost>> GetPostAsync(
        string feedUrl,
        int postId,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(postId);
        var postUrl = BuildPostUrl(feedUrl, postId);
        var result = await client
            .GetAsync(postUrl, PostDetailsJsonContext.Default.MasjidPost, cachePolicy, ct)
            .ConfigureAwait(false);

        return result switch
        {
            ApiResult<MasjidPost>.Fresh fresh => fresh with { Value = Normalize(fresh.Value, postUrl) },
            ApiResult<MasjidPost>.Cached cached => cached with { Value = Normalize(cached.Value, postUrl) },
            ApiResult<MasjidPost>.NotModified notModified => notModified with { Value = Normalize(notModified.Value, postUrl) },
            ApiResult<MasjidPost>.Stale stale => stale with { Value = Normalize(stale.Value, postUrl) },
            _ => result,
        };
    }

    internal static string BuildPostUrl(string feedUrl, int postId)
    {
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var feedUri) ||
            (feedUri.Scheme != Uri.UriSchemeHttp && feedUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Feed URL must be an absolute HTTP(S) URL.", nameof(feedUrl));
        }

        var path = feedUri.AbsolutePath.TrimEnd('/');
        var separator = path.LastIndexOf('/');
        var apiRoot = separator >= 0 ? path[..separator] : string.Empty;
        var builder = new UriBuilder(feedUri)
        {
            Path = $"{apiRoot}/posts/{postId}",
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.ToString();
    }

    private static MasjidPost Normalize(MasjidPost post, string postUrl) => post with
    {
        Title = HtmlSanitizer.ToPlainText(post.Title),
        ImageUrl = EndpointUrlResolver.Resolve(post.ImageUrl, postUrl),
    };
}