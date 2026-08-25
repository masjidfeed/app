using System.Text.Json.Serialization;

namespace MasjidApp.Notifications;

/// <summary>One persisted remote push notification.</summary>
public sealed record NotificationInboxItem(
    string NotificationId,
    string MasjidId,
    string Title,
    string Body,
    string? ImageUrl,
    string? DeepLink,
    string? Type,
    int? PostId,
    DateTimeOffset ReceivedAt,
    bool IsRead);

[JsonSerializable(typeof(List<NotificationInboxItem>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class NotificationInboxJsonContext : JsonSerializerContext;