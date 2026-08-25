using System.Text.Json.Serialization;

namespace MasjidApp.Core.Models;

/// <summary>
/// Unified WordPress post returned by post, event, and announcement endpoints.
/// Event-only fields remain empty for regular announcements.
/// </summary>
public sealed record MasjidPost
{
    /// <summary>Stable WordPress post identifier used for notification deep links.</summary>
    [JsonPropertyName("postId")]
    public int PostId { get; init; }

    /// <summary>Display title of the post.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Full rendered HTML post content.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Absolute URL of the post-detail endpoint from the current API.</summary>
    [JsonPropertyName("postUrl")]
    public string? PostUrl { get; init; }

    /// <summary>Legacy post-description URL retained for tolerant feed parsing.</summary>
    [JsonPropertyName("details")]
    public string? LegacyDetailsUrl { get; init; }

    /// <summary>Best available post-detail URL across current and legacy contracts.</summary>
    [JsonIgnore]
    public string? DetailsUrl => PostUrl ?? LegacyDetailsUrl;

    /// <summary>ISO 8601 publication timestamp with offset.</summary>
    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Optional absolute URL of the post's featured image.</summary>
    [JsonPropertyName("image")]
    public string? ImageUrl { get; init; }

    /// <summary>Absolute WordPress permalink.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Optional category tag.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>Whether this post carries event scheduling fields.</summary>
    [JsonPropertyName("isEvent")]
    public bool IsEvent { get; init; }

    /// <summary>ISO 8601 event start date/time including UTC offset.</summary>
    [JsonPropertyName("eventDateTime")]
    public DateTimeOffset? EventDateTime { get; init; }

    /// <summary>Optional event location.</summary>
    [JsonPropertyName("location")]
    public string? Location { get; init; }

    /// <summary>Optional iCalendar recurrence rule.</summary>
    [JsonPropertyName("recurrenceRule")]
    public string? RecurrenceRule { get; init; }

    /// <summary>Optional time-limited event update.</summary>
    [JsonPropertyName("alert")]
    public string? Alert { get; init; }

    /// <summary>Instant through which <see cref="Alert"/> remains visible.</summary>
    [JsonPropertyName("alertEndDateTime")]
    public DateTimeOffset? AlertEndDateTime { get; init; }
}