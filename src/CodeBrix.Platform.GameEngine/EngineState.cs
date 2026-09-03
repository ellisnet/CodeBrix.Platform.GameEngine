using System.IO.Compression;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Json.Extensions.References;
using CodeBrix.Platform.GameEngine.Assets;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets.GTS;
using CodeBrix.Platform.GameEngine.Scenes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine; //was previously: Gondwana;
/// <summary>
/// Represents the complete serializable state of the game engine, including assets, scenes, sprites,
/// audio resources, and custom data. This class provides functionality to save and load engine state
/// to/from files with support for selective state management, compression, and merge operations.
/// The state can be persisted as JSON and optionally compressed using GZip compression.
/// </summary>
[JsonReferenceable]
public sealed class EngineState
{
    private sealed class TilesheetStateEntry
    {
        [JsonInclude]
        public string? GtsPath { get; set; }

        [JsonInclude]
        public TilesheetDefinition? Definition { get; set; }
    }

    /// <summary>
    /// The current save-file schema version. Saved files are wrapped in a versioned envelope
    /// (<c>{ "schema": N, "state": { ... } }</c>) so future readers can branch. Pre-v1 (Newtonsoft)
    /// save files are unsupported by design.
    /// </summary>
    public const int CurrentSaveSchemaVersion = 1;

    /// <summary>
    /// Gets or sets the JSON serializer options template used for serializing and deserializing engine
    /// state. These options produce indented (human-readable) JSON and carry the engine's save
    /// contracts: the <see cref="Serialization.EngineSaveContractResolver"/> (object contracts for the
    /// engine's referenceable model types) plus the leaf converters for <see cref="Frame"/>,
    /// <see cref="Drawing.Animation.FrameSequence"/>, and the scene-layer tile grid.
    /// Object-reference preservation (<c>$id</c>/<c>$ref</c>) and <see cref="Tile"/> discriminator
    /// dispatch are applied by <c>CodeBrix.Json.Extensions.References.ReferenceJson</c>, which uses
    /// this as a settings template. (Do not add the polymorphism fallback converter factory here —
    /// it would take precedence over reference handling for discriminated referenceable types.)
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; set; }
        = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new Serialization.EngineSaveContractResolver(),
            Converters =
            {
                new FrameJsonConverter(),
                new Serialization.FrameSequenceJsonConverter(),
                new Serialization.SceneLayerTileArrayJsonConverter(),
                new Serialization.CollisionGroupRegistryJsonConverter(),
                new Serialization.CollisionProfileRegistryJsonConverter()
            }
        };

    /// <summary>
    /// Gets the collection of all loaded asset files (resource archives) currently registered with the engine.
    /// Asset files contain packed game resources such as images, audio, and data files that have been
    /// loaded into memory. This property provides a snapshot of the current asset files for serialization purposes.
    /// </summary>
    [JsonInclude]
    public IEnumerable<AssetsFile> AssetsFiles => AssetsFile.AllAssetsFiles;

    /// <summary>
    /// Gets a dictionary of all registered tilesheets, keyed by their unique identifiers.
    /// Tilesheets contain tile graphics and metadata used for rendering tile-based game worlds.
    /// This property provides access to the current tilesheet registry for serialization and state management.
    /// </summary>
    [JsonInclude]
    public IDictionary<string, TilesheetDefinition> Tilesheets => CaptureTilesheetDefinitions(baseDirectory: null);

    /// <summary>
    /// Gets the dictionary of all registered animation cycles, keyed by their unique identifiers.
    /// Animation cycles define sprite animation sequences including frame data, timing, and playback behavior.
    /// This property provides direct access to the cycle registry for serialization purposes.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, Cycle> Cycles => Cycle._cycles;

    /// <summary>
    /// Gets the list of all scenes currently registered with the engine.
    /// Scenes represent distinct game locations or levels, containing layers, entities, and scene-specific data.
    /// This property provides direct access to the scene collection for serialization and state management.
    /// </summary>
    [JsonInclude]
    public List<Scene> Scenes => Scene._allScenes;

    /// <summary>
    /// Gets the list of all active sprites currently managed by the sprite manager.
    /// Sprites are visual game entities that can be positioned, animated, and rendered on screen.
    /// This property provides direct access to the sprite collection for serialization purposes.
    /// </summary>
    [JsonInclude]
    public List<Sprite> Sprites => SpriteManager.Instance._spriteList;

    /// <summary>
    /// Gets the dictionary of all registered audio resources, keyed by their unique identifiers.
    /// Audio resources include sound effects, music tracks, and their associated playback settings
    /// such as volume, pan, and looping behavior. This property provides access to the audio resource
    /// registry for serialization and state management.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, AudioResource> SoundResources => AudioResourceManager.Instance.GetAll();

    /// <summary>
    /// Stores extensible, project-specific state data associated with this engine state.
    /// <para>
    /// The value bag allows games or engine extensions to persist arbitrary structured data
    /// (such as NPC state, quest progress, or custom subsystem data) without modifying the
    /// core <see cref="EngineState"/> schema.
    /// </para>
    /// <para>
    /// Values are accessed using strongly-typed <see cref="ValueKey{T}"/> instances.
    /// </para>
    /// <para>
    /// *** NOTE: This property is NOT included in the serialized JSON. ***
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// // Define keys once (typically in a static class)
    /// static readonly ValueKey&lt;Dictionary&lt;string, int&gt;&gt; NpcHitPoints =
    ///     new("npc.hitpoints");
    ///
    /// // Store values
    /// engineState.ValueBag.Set(NpcHitPoints, new Dictionary&lt;string, int&gt;
    /// {
    ///     ["npc.guard"] = 12,
    ///     ["npc.merchant"] = 8
    /// });
    ///
    /// // Retrieve values
    /// var hp = engineState.ValueBag.Get(NpcHitPoints, new Dictionary&lt;string, int&gt;());
    /// </code>
    /// </example>
    [JsonIgnore]
    public TypedValueBag ValueBag { get; set; } = new();

    /// <summary>
    /// Clears all engine state components, including assets, tilesheets, animation cycles, scenes,
    /// sprites, audio resources, and custom value bag data. This method resets the engine to a clean state
    /// by disposing or clearing all registered resources and collections. Use this when you need to
    /// completely reset the engine state, such as when loading a new game or returning to a main menu.
    /// </summary>
    internal void Clear()
    {
        AssetsFile.ClearAll();
        TilesheetRegistry.Instance.Clear();
        Cycle.ClearAllAnimationCycles();
        Scene.ClearAllScenes();
        SpriteManager.Instance.ClearImmediate();
        AudioResourceManager.Instance.Dispose();
        ValueBag.Clear();
    }

    /// <summary>
    /// Saves the current engine state to a file in JSON format with optional compression and selective
    /// state component inclusion. The saved state can later be loaded using <see cref="LoadFromFile"/>
    /// or merged using <see cref="MergeFromFile"/>.
    /// </summary>
    /// <param name="path">
    /// The file path where the engine state should be saved. The directory must exist and be writable.
    /// If the file already exists, it will be overwritten.
    /// </param>
    /// <param name="compress">
    /// If <c>true</c>, the JSON output will be compressed using GZip compression, reducing file size
    /// at the cost of additional processing time. If <c>false</c>, the JSON is written as plain text.
    /// Default is <c>false</c>.
    /// </param>
    /// <param name="separateGtsFiles"></param>
    /// <param name="parts">
    /// Specifies which parts of the engine state should be included in the saved file. Use bitwise
    /// flags from <see cref="EngineStateParts"/> to select specific components, or use
    /// <see cref="EngineStateParts.All"/> to save the complete state. Default is <see cref="EngineStateParts.All"/>.
    /// </param>
    public void SaveToFile(string path,
                           bool compress = false,
                           bool separateGtsFiles = false,
                           EngineStateParts parts = EngineStateParts.All)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Engine state path must be a non-empty string.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var baseDirectory = Path.GetDirectoryName(fullPath);

        var snapshot = BuildSnapshot(parts, baseDirectory, fullPath, separateGtsFiles);

        var envelope = new SaveFileEnvelope { Schema = CurrentSaveSchemaVersion, State = snapshot };
        var json = CodeBrix.Json.Extensions.References.ReferenceJson.Serialize(envelope, SerializerOptions);

        if (compress)
        {
            using var file = File.Create(fullPath);
            using var zip = new GZipStream(file, CompressionMode.Compress);
            using var writer = new StreamWriter(zip);
            writer.Write(json);
        }
        else
        {
            File.WriteAllText(fullPath, json);
        }
    }

    /// <summary>
    /// Loads engine state from a file, replacing the current engine state with the saved state.
    /// This method clears existing state components before loading, providing a clean slate for the
    /// loaded data. Dependencies between state parts (such as tilesheets depending on asset files)
    /// are automatically handled.
    /// </summary>
    /// <param name="path">
    /// The file path from which to load the engine state. The file must exist and contain valid
    /// serialized engine state data in JSON format.
    /// </param>
    /// <param name="compressed">
    /// If <c>true</c>, the file is expected to be GZip-compressed and will be decompressed before
    /// deserialization. If <c>false</c>, the file is read as plain text JSON. Default is <c>false</c>.
    /// </param>
    /// <param name="parts">
    /// Specifies which parts of the engine state should be loaded from the file. Use bitwise flags
    /// from <see cref="EngineStateParts"/> to select specific components. Note that dependencies
    /// are automatically included (e.g., loading tilesheets will also load asset files).
    /// Default is <see cref="EngineStateParts.All"/>.
    /// </param>
    public static void LoadFromFile(string path, bool compressed = false, EngineStateParts parts = EngineStateParts.All)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Engine state path must be a non-empty string.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var baseDirectory = Path.GetDirectoryName(fullPath);

        LoadAndApply(fullPath, compressed, clearExisting: true, overwriteExisting: true, parts, baseDirectory);
    }

    /// <summary>
    /// Loads engine state from a file and merges it with the current engine state, optionally
    /// overwriting existing items with matching identifiers. Unlike <see cref="LoadFromFile"/>,
    /// this method does not clear existing state before loading, allowing incremental state updates
    /// and data patching scenarios.
    /// </summary>
    /// <param name="path">
    /// The file path from which to load the engine state. The file must exist and contain valid
    /// serialized engine state data in JSON format.
    /// </param>
    /// <param name="compressed">
    /// If <c>true</c>, the file is expected to be GZip-compressed and will be decompressed before
    /// deserialization. If <c>false</c>, the file is read as plain text JSON. Default is <c>false</c>.
    /// </param>
    /// <param name="overwriteExisting">
    /// If <c>true</c>, items from the loaded state will replace existing items with the same
    /// identifiers (such as scene IDs or sprite nicknames). If <c>false</c>, existing items are
    /// preserved and only new items from the loaded state are added. Default is <c>false</c>.
    /// </param>
    /// <param name="parts">
    /// Specifies which parts of the engine state should be merged from the file. Use bitwise flags
    /// from <see cref="EngineStateParts"/> to select specific components. Dependencies are
    /// automatically included. Default is <see cref="EngineStateParts.All"/>.
    /// </param>
    public static void MergeFromFile(string path, bool compressed = false, bool overwriteExisting = false, EngineStateParts parts = EngineStateParts.All)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Engine state path must be a non-empty string.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var baseDirectory = Path.GetDirectoryName(fullPath);

        LoadAndApply(fullPath, compressed, clearExisting: false, overwriteExisting, parts, baseDirectory);
    }

    #region deserialization helpers

    /// <summary>
    /// Versioned save-file envelope: <c>{ "schema": N, "state": { ... } }</c>. The engine state
    /// snapshot lives under <c>state</c>; <c>schema</c> lets future readers branch.
    /// </summary>
    private sealed class SaveFileEnvelope
    {
        [JsonPropertyName("schema")]
        [JsonInclude]
        public int Schema { get; set; }

        [JsonPropertyName("state")]
        [JsonInclude]
        public EngineStateSnapshot? State { get; set; }
    }

    /// <summary>
    /// The staged load path shared by <see cref="LoadFromFile"/> and <see cref="MergeFromFile"/>.
    /// Stages exist because of load-order dependencies inside one save file: tilesheet images may
    /// come from asset packs, and <see cref="FrameJsonConverter"/> resolves tilesheets BY NAME
    /// while the object graph deserializes — so assets and tilesheets must be live in their
    /// registries before the graph (cycles/scenes/sprites/audio) is read.
    /// </summary>
    private static void LoadAndApply(
        string fullPath,
        bool compressed,
        bool clearExisting,
        bool overwriteExisting,
        EngineStateParts parts,
        string? baseDirectory)
    {
        string json = ReadJsonFile(fullPath, compressed);
        parts = NormalizeParts(parts);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Null)
        {
            // A "null" envelope: nothing to apply, but Load semantics still clear.
            if (clearExisting)
                ClearSelected(parts);
            return;
        }

        int schema = root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("schema", out var schemaElement)
                     && schemaElement.ValueKind == JsonValueKind.Number
            ? schemaElement.GetInt32()
            : 0;

        if (schema < 1 || schema > CurrentSaveSchemaVersion)
        {
            throw new NotSupportedException(
                $"Unsupported engine save-file schema version {schema}. This build supports " +
                $"versions 1 through {CurrentSaveSchemaVersion}. Pre-v1 (Newtonsoft) save files are not supported.");
        }

        bool hasState = root.TryGetProperty("state", out var stateElement)
                        && stateElement.ValueKind == JsonValueKind.Object;

        // Clear only what we're about to load.
        if (clearExisting)
            ClearSelected(parts);

        // STAGE 1: assets + tilesheets into their registries.
        var loadedAssets = new List<AssetsFile>();
        if (hasState)
        {
            if (parts.HasFlag(EngineStateParts.AssetsFiles)
                && stateElement.TryGetProperty(nameof(EngineStateSnapshot.AssetsFiles), out var assetsElement)
                && assetsElement.ValueKind == JsonValueKind.Array)
            {
                var rawAssets = CodeBrix.Json.Extensions.References.ReferenceJson
                    .Deserialize<List<AssetsFile>>(assetsElement.GetRawText(), SerializerOptions);
                loadedAssets = LoadAssetsFiles(rawAssets ?? new List<AssetsFile>(), overwriteExisting);
            }

            if (parts.HasFlag(EngineStateParts.Tilesheets)
                && stateElement.TryGetProperty(nameof(EngineStateSnapshot.Tilesheets), out var tilesheetsElement)
                && tilesheetsElement.ValueKind == JsonValueKind.Object)
            {
                var tilesheets = JsonSerializer.Deserialize<Dictionary<string, TilesheetStateEntry>>(
                    tilesheetsElement.GetRawText(), SerializerOptions);
                MergeTilesheets(tilesheets, overwriteExisting, baseDirectory);
            }
        }

        // STAGE 2: the object graph (frames now resolve against the live tilesheet registry).
        // EngineState's collections are getter-only proxies over registries, so this
        // deserializes into the settable snapshot DTO.
        bool needsGraph = parts.HasFlag(EngineStateParts.Cycles)
                          || parts.HasFlag(EngineStateParts.Scenes)
                          || parts.HasFlag(EngineStateParts.Sprites)
                          || parts.HasFlag(EngineStateParts.Audio);
        var snapshot = needsGraph && hasState
            ? CodeBrix.Json.Extensions.References.ReferenceJson
                  .Deserialize<EngineStateSnapshot>(stateElement.GetRawText(), SerializerOptions)
              ?? new EngineStateSnapshot()
            : new EngineStateSnapshot();

        // STAGE 3: merge into the live registries in dependency order.
        if (parts.HasFlag(EngineStateParts.Audio))
            MergeAudio(loadedAssets, snapshot.SoundResources, overwriteExisting);

        if (parts.HasFlag(EngineStateParts.Cycles))
            MergeCycles(snapshot.Cycles, overwriteExisting);

        if (parts.HasFlag(EngineStateParts.Scenes))
            MergeScenes(snapshot.Scenes, overwriteExisting);

        if (parts.HasFlag(EngineStateParts.Sprites))
            MergeSprites(snapshot.Sprites, overwriteExisting);
    }

    private sealed class EngineStateSnapshot
    {
        [JsonInclude] public List<AssetsFile>? AssetsFiles { get; set; }
        [JsonInclude] public Dictionary<string, TilesheetStateEntry>? Tilesheets { get; set; }
        [JsonInclude] public Dictionary<string, Cycle>? Cycles { get; set; }
        [JsonInclude] public List<Scene>? Scenes { get; set; }
        [JsonInclude] public List<Sprite>? Sprites { get; set; }
        [JsonInclude] public Dictionary<string, AudioResource>? SoundResources { get; set; }
    }

    private static EngineStateParts NormalizeParts(EngineStateParts parts)
    {
        // Tilesheets and Audio may depend on AssetsFiles for AssetIdentifier.Data
        if (parts.HasFlag(EngineStateParts.Tilesheets) ||
            parts.HasFlag(EngineStateParts.Audio))
        {
            parts |= EngineStateParts.AssetsFiles;
        }

        return parts;
    }

    private EngineStateSnapshot BuildSnapshot(EngineStateParts parts,
                                              string? baseDirectory,
                                              string engineStatePath,
                                              bool separateGtsFiles)
    {
        return new EngineStateSnapshot
        {
            AssetsFiles = parts.HasFlag(EngineStateParts.AssetsFiles)
                ? AssetsFiles.ToList()
                : null,

            Tilesheets = parts.HasFlag(EngineStateParts.Tilesheets)
                ? CaptureTilesheetEntries(
                    baseDirectory,
                    engineStatePath,
                    separateGtsFiles)
                : null,

            Cycles = parts.HasFlag(EngineStateParts.Cycles)
                ? Cycles
                : null,

            Scenes = parts.HasFlag(EngineStateParts.Scenes)
                ? Scenes
                : null,

            Sprites = parts.HasFlag(EngineStateParts.Sprites)
                ? Sprites
                : null,

            SoundResources = parts.HasFlag(EngineStateParts.Audio)
                ? SoundResources
                : null,
        };
    }

    private static string ReadJsonFile(string path, bool compressed)
    {
        if (compressed)
        {
            using var file = File.OpenRead(path);
            using var zip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(zip);
            return reader.ReadToEnd();
        }
        else
        {
            return File.ReadAllText(path);
        }
    }

    private static void ClearSelected(EngineStateParts parts)
    {
        if (parts.HasFlag(EngineStateParts.AssetsFiles))
        {
            AssetsFile.ClearAll();
            SvgResourceManager.Instance.Clear();
        }

        if (parts.HasFlag(EngineStateParts.Tilesheets))
            TilesheetRegistry.Instance.Clear();

        if (parts.HasFlag(EngineStateParts.Cycles))
            Cycle.ClearAllAnimationCycles();

        if (parts.HasFlag(EngineStateParts.Scenes))
            Scene.ClearAllScenes();

        if (parts.HasFlag(EngineStateParts.Sprites))
            SpriteManager.Instance.ClearImmediate(); // deferred disposal never runs without a cycling engine

        if (parts.HasFlag(EngineStateParts.Audio))
            AudioResourceManager.Instance.Clear(); // Clear, not Dispose: disposal latches and would make later loads skip clearing
    }

    private static List<AssetsFile> LoadAssetsFiles(IEnumerable<AssetsFile> resourceFiles, bool overwriteExisting)
    {
        // Replace raw deserialized resource files with proper loaded instances
        var loadedFiles = new List<AssetsFile>();

        foreach (var raw in resourceFiles)
        {
            try
            {
                var loaded = AssetsFile.LoadOrCreate(raw.FilePath, raw.Password, raw.UseEncryption);
                loadedFiles.Add(loaded);

                if (overwriteExisting)
                {
                    foreach (var entry in loaded.GetAllEntries().Where(e => e.AssetType == AssetTypes.Svg))
                        SvgResourceManager.Instance.Unload(entry.AssetName);
                }

                SvgResourceManager.Instance.LoadFromEngineAssetsFile(loaded);
            }
            catch (Exception ex)
            {
                Engine.Logger.LogError(ex, "Failed to load resource file '{FilePath}'", raw.FilePath);
                throw;
            }
        }

        return loadedFiles;
    }

    private static void MergeAudio(
        List<AssetsFile>? assetsFiles,
        Dictionary<string, AudioResource>? soundSpecs,
        bool overwriteExisting)
    {
        // 1) Load from asset packs (the LOADED instances from stage 1 — raw deserialized
        //    specs have no entry data)
        if (assetsFiles is not null)
        {
            foreach (var af in assetsFiles)
            {
                if (overwriteExisting)
                {
                    foreach (var entry in af.GetAllEntries())
                    {
                        if (entry.AssetType == AssetTypes.Audio)
                            AudioResourceManager.Instance.Unload(entry.AssetName);
                    }
                }

                AudioResourceManager.Instance.LoadFromEngineAssetsFile(af);
            }
        }

        // 2) Apply loose-file specs / overrides
        if (soundSpecs is null)
            return;

        foreach (var (key, spec) in soundSpecs)
        {
            // A spec without a persisted source (e.g. audio that came from an asset pack and
            // was just re-loaded above) cannot be rebuilt from disk — its saved settings are
            // applied to the pack-loaded resource instead.
            bool specHasPersistedSource = (spec.AssetIdentifier?.IsValid ?? false)
                                          || !string.IsNullOrWhiteSpace(spec.SourceFilePath);

            if (AudioResourceManager.Instance.Contains(key))
            {
                if (!overwriteExisting || !specHasPersistedSource)
                {
                    var existing = AudioResourceManager.Instance.Get(key);
                    if (existing is not null)
                    {
                        existing.Volume = spec.Volume;
                        existing.Pan = spec.Pan;
                        existing.IsLooping = spec.IsLooping;
                    }
                    continue;
                }

                AudioResourceManager.Instance.Unload(key);
            }
            else if (!specHasPersistedSource)
            {
                Engine.Logger.LogWarning(
                    "Audio spec '{Key}' has no persisted source and no loaded resource to apply to; skipping.", key);
                continue;
            }

            // Ensure the audio spec is (re)created/registered in the manager.
            spec.ReloadIntoManager();
        }
    }

    private static Dictionary<string, TilesheetDefinition> CaptureTilesheetDefinitions(string? baseDirectory)
    {
        return TilesheetRegistry.Instance
            .GetAll()
            .ToDictionary(
                kvp => kvp.Key,
                kvp => TilesheetDefinitionSerializer.FromTilesheet(
                    kvp.Value,
                    baseDirectory,
                    makePathsRelative: !string.IsNullOrWhiteSpace(baseDirectory)));
    }

    private static Dictionary<string, TilesheetStateEntry> CaptureTilesheetEntries(
        string? baseDirectory,
        string engineStatePath,
        bool separateGtsFiles)
    {
        var result = new Dictionary<string, TilesheetStateEntry>(StringComparer.Ordinal);

        foreach (var (key, tilesheet) in TilesheetRegistry.Instance.GetAll())
        {
            if (separateGtsFiles)
            {
                var gtsDirectory = GetTilesheetStateDirectory(engineStatePath);
                Directory.CreateDirectory(gtsDirectory);

                var gtsFileName = $"{SanitizeFileName(key)}.gts";
                var gtsFullPath = Path.Combine(gtsDirectory, gtsFileName);

                // Important:
                // Save the GTS using the GTS serializer, not EngineState.JsonSerializerSettings.
                // This avoids $id/$values noise in the .gts file.
                TilesheetDefinitionSerializer.Save(
                    gtsFullPath,
                    tilesheet,
                    makePathsRelative: true);

                var gtsPathForState = MakeRelativePath(
                    gtsFullPath,
                    baseDirectory);

                result[key] = new TilesheetStateEntry
                {
                    GtsPath = gtsPathForState
                };
            }
            else
            {
                result[key] = new TilesheetStateEntry
                {
                    Definition = TilesheetDefinitionSerializer.FromTilesheet(
                        tilesheet,
                        baseDirectory,
                        makePathsRelative: !string.IsNullOrWhiteSpace(baseDirectory))
                };
            }
        }

        return result;
    }

    private static string GetTilesheetStateDirectory(string engineStatePath)
    {
        var directory = Path.GetDirectoryName(engineStatePath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(engineStatePath);

        return Path.Combine(directory, $"{fileName}.tilesheets");
    }

    private static string MakeRelativePath(string path, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return path;

        return Path.GetRelativePath(
            Path.GetFullPath(baseDirectory),
            Path.GetFullPath(path));
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Guid.NewGuid().ToString("N");

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());

        return string.IsNullOrWhiteSpace(sanitized)
            ? Guid.NewGuid().ToString("N")
            : sanitized;
    }

    private static void MergeTilesheets(
        Dictionary<string, TilesheetStateEntry>? tilesheets,
        bool overwriteExisting,
        string? baseDirectory)
    {
        if (tilesheets is null || tilesheets.Count == 0)
            return;

        var registry = TilesheetRegistry.Instance.GetAll();

        foreach (var (key, entry) in tilesheets)
        {
            if (entry is null)
                continue;

            var existingKey = key;

            if (!overwriteExisting && registry.ContainsKey(existingKey))
                continue;

            Tilesheet rebuilt;

            if (!string.IsNullOrWhiteSpace(entry.GtsPath))
            {
                var gtsPath = ResolvePath(entry.GtsPath, baseDirectory);

                rebuilt = TilesheetFactory.FromDefinitionFile(gtsPath);
            }
            else if (entry.Definition is not null)
            {
                if (string.IsNullOrWhiteSpace(entry.Definition.Name))
                    entry.Definition.Name = key;

                rebuilt = TilesheetFactory.FromDefinition(
                    entry.Definition,
                    baseDirectory);
            }
            else
            {
                throw new InvalidDataException(
                    $"Tilesheet state entry '{key}' does not contain a GTS path or inline definition.");
            }

            TilesheetRegistry.Instance.Register(
                rebuilt,
                disposeReplaced: overwriteExisting);
        }
    }

    private static string ResolvePath(string path, string? baseDirectory)
    {
        if (Path.IsPathRooted(path))
            return path;

        if (string.IsNullOrWhiteSpace(baseDirectory))
            return path;

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static void MergeCycles(Dictionary<string, Cycle>? cycles, bool overwriteExisting)
    {
        if (cycles is null || cycles.Count == 0)
            return;

        foreach (var (key, cycle) in cycles)
        {
            if (!overwriteExisting && Cycle._cycles.ContainsKey(key))
                continue;

            Cycle._cycles[key] = cycle;
        }
    }

    private static void MergeScenes(List<Scene>? scenes, bool overwriteExisting)
    {
        if (scenes is null || scenes.Count == 0)
            return;

        // Index existing scenes by ID (case-sensitive; change if you prefer)
        var existingIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Scene._allScenes.Count; i++)
        {
            var id = Scene._allScenes[i].ID;
            if (!string.IsNullOrWhiteSpace(id) && !existingIndexById.ContainsKey(id))
                existingIndexById.Add(id, i);
        }

        // Avoid duplicating the same incoming ID twice (keeps last one)
        var seenIncoming = new HashSet<string>(StringComparer.Ordinal);

        foreach (var incoming in scenes)
        {
            if (incoming is null)
                continue;

            // Deserialized scenes carry state but no live wiring — rebuild layer runtime
            // structures, tile colliders, and scene<->layer event subscriptions.
            incoming.RehydrateAfterDeserialization();

            // Ensure ID exists (important if something created scenes without IDs)
            if (string.IsNullOrWhiteSpace(incoming.ID))
                incoming.ID = Guid.NewGuid().ToString();

            // If the incoming list contains the same ID multiple times, last one wins.
            if (!seenIncoming.Add(incoming.ID))
            {
                // Replace the previously added/replaced incoming with this one:
                // easiest way: treat it as overwriteExisting=true for that ID
                overwriteExisting = true;
            }

            if (existingIndexById.TryGetValue(incoming.ID, out int existingIndex))
            {
                if (!overwriteExisting)
                    continue;

                Scene._allScenes[existingIndex] = incoming;
            }
            else
            {
                existingIndexById[incoming.ID] = Scene._allScenes.Count;
                Scene._allScenes.Add(incoming);
            }
        }
    }

    private static void MergeSprites(List<Sprite>? sprites, bool overwriteExisting)
    {
        if (sprites is null || sprites.Count == 0)
            return;

        var existingIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < SpriteManager.Instance._spriteList.Count; i++)
        {
            var id = SpriteManager.Instance._spriteList[i].Nickname;
            if (!string.IsNullOrWhiteSpace(id) && !existingIndexById.ContainsKey(id))
                existingIndexById.Add(id, i);
        }

        var seenIncoming = new HashSet<string>(StringComparer.Ordinal);

        foreach (var incoming in sprites)
        {
            if (incoming is null)
                continue;

            // Deserialized sprites carry state but no live wiring — rebuild the animator,
            // movement controller, and collider (registration below stays the merge's job).
            incoming.RehydrateAfterDeserialization();

            if (string.IsNullOrWhiteSpace(incoming.Nickname))
                incoming.Nickname = Guid.NewGuid().ToString();

            if (!seenIncoming.Add(incoming.Nickname))
            {
                // Same-ID appears again in the incoming list: last one wins.
                overwriteExisting = true;
            }

            if (existingIndexById.TryGetValue(incoming.Nickname, out int existingIndex))
            {
                if (!overwriteExisting)
                    continue;

                SpriteManager.Instance._spriteList[existingIndex] = incoming;
            }
            else
            {
                existingIndexById[incoming.Nickname] = SpriteManager.Instance._spriteList.Count;
                SpriteManager.Instance.AddSprite(incoming);
            }
        }
    }

    #endregion deserialization helpers
}
