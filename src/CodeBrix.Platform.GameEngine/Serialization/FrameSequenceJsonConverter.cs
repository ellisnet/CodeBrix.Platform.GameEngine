using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Animation;

namespace CodeBrix.Platform.GameEngine.Serialization; //CodeBrix (not from Gondwana)

/// <summary>
/// Serializes <see cref="FrameSequence"/> as <c>{ "cycleType", "frames": [ ... ] }</c>.
/// Needed because the struct implements <c>IEnumerable&lt;Frame&gt;</c> (so stock
/// System.Text.Json would classify it as a collection and lose <see cref="FrameSequence.SequenceCycleType"/>)
/// and keeps its frame list in a non-public field. Frames themselves go through
/// <see cref="FrameJsonConverter"/>.
/// </summary>
internal sealed class FrameSequenceJsonConverter : JsonConverter<FrameSequence>
{
    public override void Write(Utf8JsonWriter writer, FrameSequence value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("cycleType", value.SequenceCycleType.ToString());
        writer.WritePropertyName("frames");
        writer.WriteStartArray();
        foreach (var frame in value.FrameList)
        {
            JsonSerializer.Serialize(writer, frame, options);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public override FrameSequence Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an object for a FrameSequence.");
        }

        var cycleType = CycleType.Simple;
        var frames = new List<Frame>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Malformed FrameSequence object.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "cycleType":
                    cycleType = Enum.Parse<CycleType>(reader.GetString() ?? nameof(CycleType.Simple));
                    break;

                case "frames":
                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        throw new JsonException("FrameSequence 'frames' must be an array.");
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        frames.Add(JsonSerializer.Deserialize<Frame>(ref reader, options));
                    }

                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        return new FrameSequence(frames) { SequenceCycleType = cycleType };
    }
}
