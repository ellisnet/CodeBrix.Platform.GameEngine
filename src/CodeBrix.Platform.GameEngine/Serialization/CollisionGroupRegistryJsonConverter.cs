using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Platform.GameEngine.Physics.Collisions;

namespace CodeBrix.Platform.GameEngine.Serialization; //CodeBrix (not from Gondwana)

/// <summary>
/// Serializes <see cref="CollisionGroupRegistry"/> as <c>{ "groups": { name: bit, ... },
/// "nextBit": n }</c>. Needed because the registry keeps its state in non-public fields and
/// must be reconstructed through its internal constructor so its case-insensitive group
/// lookup is preserved.
/// </summary>
internal sealed class CollisionGroupRegistryJsonConverter : JsonConverter<CollisionGroupRegistry>
{
    public override void Write(Utf8JsonWriter writer, CollisionGroupRegistry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("groups");
        writer.WriteStartObject();
        foreach (var (name, bit) in value.GroupsForSerialization)
        {
            writer.WriteNumber(name, bit);
        }

        writer.WriteEndObject();
        writer.WriteNumber("nextBit", value.NextBitForSerialization);
        writer.WriteEndObject();
    }

    public override CollisionGroupRegistry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an object for a CollisionGroupRegistry.");
        }

        var groups = new Dictionary<string, int>();
        int nextBit = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Malformed CollisionGroupRegistry object.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "groups":
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        throw new JsonException("CollisionGroupRegistry 'groups' must be an object.");
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        var groupName = reader.GetString()!;
                        reader.Read();
                        groups[groupName] = reader.GetInt32();
                    }

                    break;

                case "nextBit":
                    nextBit = reader.GetInt32();
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        return new CollisionGroupRegistry(groups, nextBit);
    }
}
