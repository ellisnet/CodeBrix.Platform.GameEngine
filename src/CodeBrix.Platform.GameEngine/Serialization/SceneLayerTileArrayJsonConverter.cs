using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.Serialization; //CodeBrix (not from Gondwana)

/// <summary>
/// Serializes a <see cref="SceneLayer"/>'s tile grid (<c>SceneLayerTile[,]</c>) as
/// <c>{ "columns", "rows", "tiles": [ ... ] }</c> in column-major order — System.Text.Json
/// has no support for multi-dimensional arrays. Each tile flows through the operation's
/// normal pipeline, so tiles keep their <c>$id</c>/<c>$ref</c> identity (including the
/// tile → parent-layer back-reference).
/// </summary>
internal sealed class SceneLayerTileArrayJsonConverter : JsonConverter<SceneLayerTile[,]>
{
    public override void Write(Utf8JsonWriter writer, SceneLayerTile[,] value, JsonSerializerOptions options)
    {
        int columns = value.GetLength(0);
        int rows = value.GetLength(1);

        writer.WriteStartObject();
        writer.WriteNumber("columns", columns);
        writer.WriteNumber("rows", rows);
        writer.WritePropertyName("tiles");
        writer.WriteStartArray();
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                JsonSerializer.Serialize(writer, value[x, y], options);
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public override SceneLayerTile[,] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an object for a scene-layer tile grid.");
        }

        int columns = -1;
        int rows = -1;
        SceneLayerTile?[,]? tiles = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Malformed scene-layer tile grid object.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "columns":
                    columns = reader.GetInt32();
                    break;

                case "rows":
                    rows = reader.GetInt32();
                    break;

                case "tiles":
                    if (columns < 0 || rows < 0)
                    {
                        throw new JsonException("Tile grid dimensions must precede the 'tiles' array.");
                    }

                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        throw new JsonException("Tile grid 'tiles' must be an array.");
                    }

                    tiles = new SceneLayerTile?[columns, rows];
                    for (int x = 0; x < columns; x++)
                    {
                        for (int y = 0; y < rows; y++)
                        {
                            if (!reader.Read() || reader.TokenType == JsonTokenType.EndArray)
                            {
                                throw new JsonException("Tile grid 'tiles' array is shorter than columns*rows.");
                            }

                            tiles[x, y] = JsonSerializer.Deserialize<SceneLayerTile>(ref reader, options);
                        }
                    }

                    if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                    {
                        throw new JsonException("Tile grid 'tiles' array is longer than columns*rows.");
                    }

                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        if (tiles is null)
        {
            if (columns < 0 || rows < 0)
            {
                throw new JsonException("Scene-layer tile grid is missing its dimensions.");
            }

            tiles = new SceneLayerTile?[columns, rows];
        }

        return tiles!;
    }
}
