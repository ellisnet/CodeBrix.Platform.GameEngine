using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

/// <summary>
/// Maps the file extensions found inside Kenney asset bundles onto the engine's
/// <see cref="GameAssetKind"/>, and decides which of them the provider can turn into an engine
/// object.
/// </summary>
/// <remarks>
/// Classification is by extension alone, so it never reads a file. <see cref="GameAssetKind.SpriteAtlas"/>
/// is never returned: an atlas is recognized only when an <c>.xml</c> file turns out to be a
/// TextureAtlas document whose sheet image exists, which the pack index decides while it builds its
/// catalog.
/// </remarks>
internal static class AssetClassifier
{
    private static readonly Dictionary<string, GameAssetKind> KindsByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["png"] = GameAssetKind.Image,
            ["jpg"] = GameAssetKind.Image,
            ["jpeg"] = GameAssetKind.Image,
            ["gif"] = GameAssetKind.Image,
            ["bmp"] = GameAssetKind.Image,
            ["webp"] = GameAssetKind.Image,
            ["svg"] = GameAssetKind.Vector,
            ["glb"] = GameAssetKind.Model3D,
            ["gltf"] = GameAssetKind.Model3D,
            ["fbx"] = GameAssetKind.Model3D,
            ["obj"] = GameAssetKind.Model3D,
            ["dae"] = GameAssetKind.Model3D,
            ["stl"] = GameAssetKind.Model3D,
            //A material file is part of a model, not an asset of its own, but it is 3D content and
            //  keeping it under Model3D means one kind covers a model and everything beside it
            ["mtl"] = GameAssetKind.Model3D,
            ["ogg"] = GameAssetKind.Audio,
            ["wav"] = GameAssetKind.Audio,
            ["mp3"] = GameAssetKind.Audio,
            ["flac"] = GameAssetKind.Audio,
            ["ttf"] = GameAssetKind.Font,
            ["otf"] = GameAssetKind.Font,
            ["tmx"] = GameAssetKind.TiledMap,
            ["tsx"] = GameAssetKind.TiledMap,
            ["txt"] = GameAssetKind.Document,
            ["html"] = GameAssetKind.Document,
            ["htm"] = GameAssetKind.Document,
            ["xml"] = GameAssetKind.Document,
            ["md"] = GameAssetKind.Document,
            ["url"] = GameAssetKind.Document,
            ["pdf"] = GameAssetKind.Document,
            ["zip"] = GameAssetKind.Archive,
            //Recognized, but nothing the engine can use: obsolete Flash exports beside every SVG,
            //  authoring sources, web font formats, other engines' project files and stray
            //  desktop.ini files, all of which are real Kenney bundle content
            //A .gltf model keeps its vertex data in a satellite .bin file, which the reader resolves
            //  from the model itself; it is recognized so that it is not reported as a file type the
            //  provider does not know
            ["bin"] = GameAssetKind.Other,
            ["swf"] = GameAssetKind.Other,
            ["blend"] = GameAssetKind.Other,
            ["3ds"] = GameAssetKind.Other,
            ["skp"] = GameAssetKind.Other,
            ["ai"] = GameAssetKind.Other,
            ["mat"] = GameAssetKind.Other,
            ["woff"] = GameAssetKind.Other,
            ["woff2"] = GameAssetKind.Other,
            ["ini"] = GameAssetKind.Other,
            ["unitypackage"] = GameAssetKind.Other,
            ["capx"] = GameAssetKind.Other,
            ["c3p"] = GameAssetKind.Other,
            ["tres"] = GameAssetKind.Other,
            ["tscn"] = GameAssetKind.Other,
            ["gd"] = GameAssetKind.Other,
            ["godot"] = GameAssetKind.Other,
            ["stex"] = GameAssetKind.Other,
            ["oggstr"] = GameAssetKind.Other,
            ["import"] = GameAssetKind.Other,
        };

    private static readonly HashSet<string> MaterializableModelExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "glb", "gltf" };

    /// <summary>
    /// Classifies a file by its extension.
    /// </summary>
    /// <param name="path">The archive-relative path of the file.</param>
    /// <returns>
    /// The kind the extension maps to, or <see cref="GameAssetKind.Unknown"/> for a file with no
    /// extension or an extension Kenney bundles are not known to carry.
    /// </returns>
    public static GameAssetKind Classify(string? path)
    {
        string extension = KenneyArchivePath.GetExtension(path);
        if (extension.Length == 0) { return GameAssetKind.Unknown; }

        return KindsByExtension.TryGetValue(extension, out GameAssetKind kind)
            ? kind
            : GameAssetKind.Unknown;
    }

    /// <summary>
    /// Determines whether the provider can turn an asset of this kind and extension into an engine
    /// object.
    /// </summary>
    /// <param name="kind">The asset kind.</param>
    /// <param name="extension">The lower-case file extension without a leading dot; only consulted for <see cref="GameAssetKind.Model3D"/>.</param>
    /// <returns><see langword="true"/> when the asset can be materialized; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Image, sprite-atlas, audio, font, vector and tile-map assets are always materializable. A 3D
    /// model is materializable only in glTF form (<c>.glb</c> or <c>.gltf</c>); the .fbx, .obj, .mtl,
    /// .dae and .stl copies Kenney ships beside it are listed for discovery only. Documents, nested
    /// archives and everything under <see cref="GameAssetKind.Other"/> or
    /// <see cref="GameAssetKind.Unknown"/> are never materializable.
    /// </remarks>
    public static bool IsMaterializable(GameAssetKind kind, string? extension) => kind switch
    {
        GameAssetKind.Image => true,
        GameAssetKind.SpriteAtlas => true,
        GameAssetKind.Audio => true,
        GameAssetKind.Font => true,
        GameAssetKind.Vector => true,
        GameAssetKind.TiledMap => true,
        GameAssetKind.Model3D =>
            extension is not null && MaterializableModelExtensions.Contains(extension),
        _ => false,
    };
}
