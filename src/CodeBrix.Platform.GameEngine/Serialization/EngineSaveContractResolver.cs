using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodeBrix.Platform.GameEngine.Assets;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.Serialization; //CodeBrix (not from Gondwana)

/// <summary>
/// The contract resolver behind <see cref="EngineState.SerializerOptions"/>: it hand-builds
/// System.Text.Json object contracts for the engine's <c>[JsonReferenceable]</c> model types
/// so the save/load graph round-trips faithfully.
/// </summary>
/// <remarks>
/// <para>
/// Stock System.Text.Json cannot round-trip these types as they are written: <see cref="Scene"/>
/// and <see cref="SceneLayer"/> implement <see cref="System.Collections.IEnumerable"/> (so STJ
/// classifies them as collections and drops their members), several persistent members live in
/// non-public fields annotated for serialization (a Newtonsoft-era pattern this engine port
/// keeps), and some types construct only through parameterized or side-effecting constructors.
/// </para>
/// <para>
/// For each handled type this resolver supplies an object-kind contract with: a deserialization
/// constructor free of registry side effects, every member carrying <c>[JsonInclude]</c> or
/// <c>[JsonPropertyName]</c> (any visibility, fields included, readonly included), and every
/// public read/write property not marked <c>[JsonIgnore]</c>. All other types fall through to
/// the default resolver. When a registered converter claims a handled type (the
/// reference-aware converter during a save/load operation), the resolver defers to it so
/// <c>$id</c>/<c>$ref</c> handling stays in charge.
/// </para>
/// </remarks>
public sealed class EngineSaveContractResolver : DefaultJsonTypeInfoResolver
{
    private static readonly Dictionary<Type, Func<object>> HandledTypes = new()
    {
        [typeof(Scene)] = Scene.CreateForDeserialization,
        [typeof(SceneLayer)] = SceneLayer.CreateForDeserialization,
        [typeof(SceneLayerTile)] = () => new SceneLayerTile(),
        [typeof(Sprite)] = () => new Sprite(),
        [typeof(Cycle)] = Cycle.CreateForDeserialization,
        [typeof(AudioResource)] = () => new AudioResource(),
        [typeof(AssetsFile)] = () => new AssetsFile(),
    };

    /// <inheritdoc />
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        if (!HandledTypes.TryGetValue(type, out var factory))
        {
            return base.GetTypeInfo(type, options);
        }

        // During a save/load operation the reference-aware converter factory is registered in
        // the options and must own these types ($id/$ref envelopes); the hand-built contract
        // is only for the metadata options that converter reads members through.
        foreach (var converter in options.Converters)
        {
            if (converter.CanConvert(type))
            {
                return base.GetTypeInfo(type, options);
            }
        }

        return BuildObjectContract(type, options, factory);
    }

    private static JsonTypeInfo BuildObjectContract(Type type, JsonSerializerOptions options, Func<object> factory)
    {
        var buildMethod = typeof(EngineSaveContractResolver)
            .GetMethod(nameof(BuildObjectContractCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type);
        return (JsonTypeInfo)buildMethod.Invoke(null, new object[] { options, factory })!;
    }

    private static JsonTypeInfo BuildObjectContractCore<T>(JsonSerializerOptions options, Func<object> factory)
        where T : notnull
    {
        var objectInfo = new JsonObjectInfoValues<T>
        {
            ObjectCreator = () => (T)factory(),
            PropertyMetadataInitializer = _ => BuildProperties<T>(options),
        };

        return JsonMetadataServices.CreateObjectInfo(options, objectInfo);
    }

    private static JsonPropertyInfo[] BuildProperties<T>(JsonSerializerOptions options)
    {
        // Most-derived declaration wins for a JSON name ('new'/override property shadowing).
        var byName = new Dictionary<string, JsonPropertyInfo>(StringComparer.Ordinal);
        const BindingFlags declared =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        for (var level = typeof(T); level is not null && level != typeof(object); level = level.BaseType)
        {
            foreach (var property in level.GetProperties(declared))
            {
                if (property.GetIndexParameters().Length > 0 || !IncludeProperty(property))
                {
                    continue;
                }

                var name = JsonName(property);
                if (!byName.ContainsKey(name))
                {
                    byName[name] = CreateMemberInfo<T>(options, name, property.PropertyType,
                        getter: property.GetMethod is null ? null : property.GetValue,
                        setter: property.SetMethod is null ? null : property.SetValue);
                }
            }

            foreach (var field in level.GetFields(declared))
            {
                if (field.IsStatic || field.IsLiteral || !IncludeField(field))
                {
                    continue;
                }

                var name = JsonName(field);
                if (!byName.ContainsKey(name))
                {
                    byName[name] = CreateMemberInfo<T>(options, name, field.FieldType,
                        getter: field.GetValue,
                        setter: field.SetValue); // reflection sets instance readonly fields too
                }
            }
        }

        var result = new JsonPropertyInfo[byName.Count];
        byName.Values.CopyTo(result, 0);
        return result;
    }

    private static bool IncludeProperty(PropertyInfo property)
    {
        if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
        {
            return false;
        }

        if (property.GetCustomAttribute<JsonIncludeAttribute>() is not null
            || property.GetCustomAttribute<JsonPropertyNameAttribute>() is not null)
        {
            return true;
        }

        // Default convention: public read/write properties round-trip.
        return property.GetMethod?.IsPublic == true && property.SetMethod is not null;
    }

    private static bool IncludeField(FieldInfo field)
        => field.GetCustomAttribute<JsonIgnoreAttribute>() is null
           && (field.GetCustomAttribute<JsonIncludeAttribute>() is not null
               || field.GetCustomAttribute<JsonPropertyNameAttribute>() is not null);

    private static string JsonName(MemberInfo member)
        => member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? member.Name;

    private static JsonPropertyInfo CreateMemberInfo<T>(
        JsonSerializerOptions options,
        string name,
        Type memberType,
        Func<object?, object?>? getter,
        Action<object?, object?>? setter)
    {
        var method = typeof(EngineSaveContractResolver)
            .GetMethod(nameof(CreateMemberInfoCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T), memberType);
        return (JsonPropertyInfo)method.Invoke(null, new object?[] { options, name, getter, setter })!;
    }

    private static JsonPropertyInfo CreateMemberInfoCore<TDeclaring, TMember>(
        JsonSerializerOptions options,
        string name,
        Func<object?, object?>? getter,
        Action<object?, object?>? setter)
    {
        var values = new JsonPropertyInfoValues<TMember>
        {
            DeclaringType = typeof(TDeclaring),
            PropertyName = name,
            IsProperty = true,
            IsPublic = true,
            HasJsonInclude = true,
            Getter = getter is null ? null : obj => (TMember)getter(obj)!,
            Setter = setter is null ? null : (obj, value) => setter(obj, value),
        };

        return JsonMetadataServices.CreatePropertyInfo(options, values);
    }
}
