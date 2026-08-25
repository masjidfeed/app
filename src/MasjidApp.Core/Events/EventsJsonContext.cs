using System.Text.Json.Serialization;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Events;

/// <summary>
/// AOT-safe, source-generated JSON serialization context for the events domain.
/// Used when calling <see cref="MasjidApp.Core.Http.IMasjidApiClient.GetAsync{T}"/>
/// so that the events list can be deserialized without reflection (TC-07, trimming).
/// </summary>
[JsonSerializable(typeof(List<MasjidPost>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public sealed partial class EventsJsonContext : JsonSerializerContext { }
