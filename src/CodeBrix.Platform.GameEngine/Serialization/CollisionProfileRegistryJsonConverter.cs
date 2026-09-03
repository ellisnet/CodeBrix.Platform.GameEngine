using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Platform.GameEngine.Physics.Collisions;

namespace CodeBrix.Platform.GameEngine.Serialization; //CodeBrix (not from Gondwana)

/// <summary>
/// Serializes <see cref="CollisionProfileRegistry"/> as <c>{ "profiles": { name: { "collisionGroup":
/// g, "collidesWith": [ ... ], "collidesWithAll": b }, ... } }</c>. Needed because the registry
/// keeps its state in a non-public field and must be reconstructed through its internal constructor
/// so its case-insensitive profile lookup — and the standard profiles — are preserved.
/// </summary>
internal sealed class CollisionProfileRegistryJsonConverter : JsonConverter<CollisionProfileRegistry>
{
    public override void Write(Utf8JsonWriter writer, CollisionProfileRegistry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("profiles");
        writer.WriteStartObject();

        foreach (var (name, profile) in value.ProfilesForSerialization)
        {
            writer.WritePropertyName(name);
            writer.WriteStartObject();
            writer.WriteString("collisionGroup", profile.CollisionGroup);

            writer.WritePropertyName("collidesWith");
            writer.WriteStartArray();

            foreach (var groupName in profile.CollidesWith)
            {
                writer.WriteStringValue(groupName);
            }

            writer.WriteEndArray();
            writer.WriteBoolean("collidesWithAll", profile.CollidesWithAll);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    public override CollisionProfileRegistry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an object for a CollisionProfileRegistry.");
        }

        var profiles = new Dictionary<string, CollisionProfile>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Malformed CollisionProfileRegistry object.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            if (propertyName != "profiles")
            {
                reader.Skip();
                continue;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("CollisionProfileRegistry 'profiles' must be an object.");
            }

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString()!;
                reader.Read();
                profiles[name] = ReadProfile(ref reader, name);
            }
        }

        return new CollisionProfileRegistry(profiles);
    }

    private static CollisionProfile ReadProfile(ref Utf8JsonReader reader, string name)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Collision profile '{name}' must be an object.");
        }

        var collisionGroup = string.Empty;
        var collidesWith = new List<string>();
        var collidesWithAll = false;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Malformed collision profile '{name}'.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "collisionGroup":
                    collisionGroup = reader.GetString() ?? string.Empty;
                    break;

                case "collidesWith":
                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        throw new JsonException($"Collision profile '{name}' has a malformed 'collidesWith'.");
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        collidesWith.Add(reader.GetString()!);
                    }

                    break;

                case "collidesWithAll":
                    collidesWithAll = reader.GetBoolean();
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        return new CollisionProfile(name, collisionGroup, collidesWith, collidesWithAll);
    }
}
