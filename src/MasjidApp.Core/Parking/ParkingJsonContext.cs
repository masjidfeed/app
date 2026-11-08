using System.Text.Json.Serialization;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Parking;

// AOT-safe JSON serializer context for the GET /parking-config document.

/// <summary>
/// Source-generated JSON serializer context for the parking map document.
/// </summary>
[JsonSerializable(typeof(ParkingConfig))]
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public sealed partial class ParkingJsonContext : JsonSerializerContext
{
}
