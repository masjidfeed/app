using System.Text.Json.Serialization;
using MasjidApp.Core.Models;

namespace MasjidApp.Push;

internal sealed record PushRegistrationRequest(
    [property: JsonPropertyName("masjidId")] string MasjidId,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("previousToken")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreviousToken = null);

internal sealed record PushRegistrationState(
    string MasjidId,
    string RegistrationUrl,
    string Token,
    FirebaseConfig Firebase);

[JsonSerializable(typeof(PushRegistrationRequest))]
[JsonSerializable(typeof(PushRegistrationState))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PushJsonContext : JsonSerializerContext;