using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Parking;

/// <summary>
/// Converts <c>zoneIds</c>, which is either <c>"all"</c> or a list of zone ids,
/// into a <see cref="ParkingZoneIds"/> value.
/// </summary>
public sealed class ParkingZoneIdsConverter : JsonConverter<ParkingZoneIds>
{
    /// <inheritdoc/>
    public override ParkingZoneIds? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var single = reader.GetString();
            return single is null ? null : new ParkingZoneIds(single, null);
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var ids = JsonSerializer.Deserialize(ref reader, JsonListString);
            return ids is null ? null : new ParkingZoneIds(null, ids);
        }

        throw new JsonException(
            "zoneIds must be the string \"all\" or a list of zone ids.");
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        ParkingZoneIds value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.IsAll)
        {
            writer.WriteStringValue("all");
            return;
        }

        JsonSerializer.Serialize(writer, value.List, JsonListString);
    }

    private static readonly JsonTypeInfo<List<string>> JsonListString =
        ParkingJsonContext.Default.ListString;
}
