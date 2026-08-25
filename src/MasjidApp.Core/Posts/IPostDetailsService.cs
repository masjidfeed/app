using MasjidApp.Core.Http;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Posts;

/// <summary>Loads one post directly for notification deep-link navigation.</summary>
public interface IPostDetailsService
{
    Task<ApiResult<MasjidPost>> GetPostAsync(
        string feedUrl,
        int postId,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);
}