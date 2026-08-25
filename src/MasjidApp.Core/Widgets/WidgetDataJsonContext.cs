using System.Text.Json.Serialization;

namespace MasjidApp.Core.Widgets;

// TC-04, FR-WG-01: AOT-safe JSON serialization for the widget data snapshot.
// Source generation avoids reflection at runtime, which is important for both
// performance and compatibility with iOS/Android native widget processes.

/// <summary>
/// Source-generated <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// for <see cref="WidgetData"/> and <see cref="WidgetPrayerEntry"/>.
/// </summary>
[JsonSerializable(typeof(WidgetData))]
[JsonSerializable(typeof(WidgetDayData))]
[JsonSerializable(typeof(List<WidgetDayData>))]
[JsonSerializable(typeof(List<WidgetPrayerEntry>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class WidgetDataJsonContext : JsonSerializerContext { }
