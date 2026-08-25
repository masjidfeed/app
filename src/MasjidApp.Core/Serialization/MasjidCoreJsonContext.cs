using System.Text.Json.Serialization;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Serialization;

// Source-generated JSON context for the Masjid API config model.
// Used by MasjidContext when calling IMasjidApiClient.GetAsync<MasjidConfig>.

/// <summary>
/// AOT-safe JSON serializer context for <see cref="MasjidConfig"/> and
/// <see cref="MasjidStoredEntry"/>.  These types are used across Batch 02 and
/// all downstream feature batches that read from <c>IMasjidContext</c>.
/// </summary>
[JsonSerializable(typeof(MasjidConfig))]
[JsonSerializable(typeof(List<MasjidStoredEntry>))]
[JsonSerializable(typeof(CachedImage))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public sealed partial class MasjidCoreJsonContext : JsonSerializerContext
{
}
