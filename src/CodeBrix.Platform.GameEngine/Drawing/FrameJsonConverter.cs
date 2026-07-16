using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Platform.GameEngine.Drawing; //was previously: Gondwana.Drawing;
/// <summary>
/// Serializes a Frame as a lightweight tilesheet/region/coordinate reference
/// instead of serializing the full Tilesheet object graph. This is a leaf converter
/// (no polymorphism or reference handling), ported from Newtonsoft to System.Text.Json.
/// </summary>
internal sealed class FrameJsonConverter : JsonConverter<Frame>
{
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Frame value, JsonSerializerOptions options)
    {
        if (value.Tilesheet is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("tilesheet", value.Tilesheet.Name);
        writer.WriteString("regionName", value.RegionName);
        writer.WriteNumber("xTile", value.XTile);
        writer.WriteNumber("yTile", value.YTile);
        writer.WriteEndObject();
    }

    /// <inheritdoc />
    public override Frame Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var tilesheetName = root.TryGetProperty("tilesheet", out var tsEl) ? tsEl.GetString() : null;
        var regionName = (root.TryGetProperty("regionName", out var rnEl) ? rnEl.GetString() : null)
            ?? TilesheetRegion.DefaultRegionName;

        var xTile = root.TryGetProperty("xTile", out var xEl) && xEl.ValueKind == JsonValueKind.Number
            ? xEl.GetInt32() : 0;
        var yTile = root.TryGetProperty("yTile", out var yEl) && yEl.ValueKind == JsonValueKind.Number
            ? yEl.GetInt32() : 0;

        if (string.IsNullOrWhiteSpace(tilesheetName))
            throw new JsonException("Frame is missing required tilesheet name.");

        var tilesheet = TilesheetRegistry.Instance.GetOrNull(tilesheetName);

        if (tilesheet is null)
        {
            throw new JsonException(
                $"Could not resolve Tilesheet '{tilesheetName}' while deserializing Frame.");
        }

        return new Frame(
            tilesheet,
            regionName,
            xTile,
            yTile);
    }
}
