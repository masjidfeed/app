using System.Text.Json.Serialization;

namespace MasjidApp.Core.SalahApi;

// API-07, NFR-RL-01: AOT/trimming-safe JSON context for the SalahAPI 1.1 document.

/// <summary>
/// Source-generated JSON serializer context for <see cref="SalahApiDocument"/> and
/// all nested types.  Pass <c>SalahApiJsonContext.Default.SalahApiDocument</c> to
/// <c>IMasjidApiClient.GetAsync</c> to keep the pipeline AOT/trimming-safe.
/// </summary>
[JsonSerializable(typeof(SalahApiDocument))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public sealed partial class SalahApiJsonContext : JsonSerializerContext
{
}
