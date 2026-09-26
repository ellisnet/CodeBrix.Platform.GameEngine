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
/// <see cref="FrameJsonConverter"/>. A sequence whose frames carry their own durations also writes
/// <c>"frameDurations": [ seconds | null, ... ]</c> (one entry per frame, null = the cycle's
/// ThrottleTime); an untimed sequence omits it, so its save keeps the earlier shape, and a save
/// without it loads as an untimed sequence.
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

        var durations = value.GetDurationsForSave();
        if (durations is not null)
        {
            writer.WritePropertyName("frameDurations");
            writer.WriteStartArray();
            foreach (var duration in durations)
            {
                if (duration is { } seconds)
                {
                    writer.WriteNumberValue(seconds);
                }
                else
                {
                    writer.WriteNullValue();
                }
            }

            writer.WriteEndArray();
        }

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
        List<double?>? durations = null;

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

                case "frameDurations":
                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        throw new JsonException("FrameSequence 'frameDurations' must be an array.");
                    }

                    durations = new List<double?>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        durations.Add(reader.TokenType == JsonTokenType.Null ? null : reader.GetDouble());
                    }

                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        var sequence = new FrameSequence(frames) { SequenceCycleType = cycleType };

        if (durations is not null)
        {
            try
            {
                sequence.SetDurationsFromSave(durations);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new JsonException("FrameSequence 'frameDurations' holds a duration that is not positive.", ex);
            }
        }

        return sequence;
    }
}
