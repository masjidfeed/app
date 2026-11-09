using System.Text.Json.Serialization;

namespace MasjidApp.Push;

internal sealed record PushRegistrationRequest(
    [property: JsonPropertyName("masjidId")] string MasjidId,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("previousToken")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreviousToken = null,
    [property: JsonPropertyName("registrationMode")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationMode = null);

/// <summary>Canonical topic metadata returned by the hosted push gateway in <c>client</c> registration mode.</summary>
internal sealed record PushTopicKey(
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("topic")] string Topic,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("expires_at")] string? ExpiresAt = null);

/// <summary>Registration response: <c>registered: true</c> for server-side subscription, or a <c>topicKey</c>
/// the app must subscribe to itself when the site delivers through the hosted MasjidFeed push service.</summary>
internal sealed record PushRegistrationResponse(
    [property: JsonPropertyName("registered")] bool Registered,
    [property: JsonPropertyName("topicKey")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PushTopicKey? TopicKey = null);

/// <summary>Firebase options come from the build configuration (<see cref="MasjidFeedFirebase"/>), so only
/// the registration coordinates and the locally subscribed topic are persisted here.</summary>
internal sealed record PushRegistrationState(
    string MasjidId,
    string RegistrationUrl,
    string Token,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Topic = null);

[JsonSerializable(typeof(PushRegistrationRequest))]
[JsonSerializable(typeof(PushRegistrationState))]
[JsonSerializable(typeof(PushRegistrationResponse))]
[JsonSerializable(typeof(PushTopicKey))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PushJsonContext : JsonSerializerContext;
