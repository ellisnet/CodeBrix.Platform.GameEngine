using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates the extension-to-kind table and the rule that decides what the provider can materialize.
/// </summary>
public class AssetClassifierTests
{
    [Theory]
    [InlineData("PNG/Default/ballBlue.png", GameAssetKind.Image)]
    [InlineData("Preview.JPG", GameAssetKind.Image)]
    [InlineData("art/sprite.jpeg", GameAssetKind.Image)]
    [InlineData("art/sprite.gif", GameAssetKind.Image)]
    [InlineData("art/sprite.bmp", GameAssetKind.Image)]
    [InlineData("art/sprite.webp", GameAssetKind.Image)]
    [InlineData("Vector/icons.svg", GameAssetKind.Vector)]
    [InlineData("Audio/laser.ogg", GameAssetKind.Audio)]
    [InlineData("Audio/laser.wav", GameAssetKind.Audio)]
    [InlineData("Audio/laser.mp3", GameAssetKind.Audio)]
    [InlineData("Audio/laser.flac", GameAssetKind.Audio)]
    [InlineData("Fonts/Kenney Space.ttf", GameAssetKind.Font)]
    [InlineData("Fonts/Kenney Space.otf", GameAssetKind.Font)]
    [InlineData("Tiled/map.tmx", GameAssetKind.TiledMap)]
    [InlineData("Tiled/tiles.tsx", GameAssetKind.TiledMap)]
    [InlineData("Models/GLB format/character.glb", GameAssetKind.Model3D)]
    [InlineData("Models/character.gltf", GameAssetKind.Model3D)]
    [InlineData("Models/character.fbx", GameAssetKind.Model3D)]
    [InlineData("Models/character.obj", GameAssetKind.Model3D)]
    [InlineData("Models/character.mtl", GameAssetKind.Model3D)]
    [InlineData("Models/character.dae", GameAssetKind.Model3D)]
    [InlineData("Models/character.stl", GameAssetKind.Model3D)]
    [InlineData("License.txt", GameAssetKind.Document)]
    [InlineData("Overview.html", GameAssetKind.Document)]
    [InlineData("Spritesheet/sheet.xml", GameAssetKind.Document)]
    [InlineData("Visit Kenney.url", GameAssetKind.Document)]
    [InlineData("Other/Fonts/Webfonts A.zip", GameAssetKind.Archive)]
    [InlineData("Models/glTF format/character.bin", GameAssetKind.Other)]
    [InlineData("Vector/icons.swf", GameAssetKind.Other)]
    [InlineData("Extras/character.blend", GameAssetKind.Other)]
    [InlineData("Audio/desktop.ini", GameAssetKind.Other)]
    [InlineData("Godot/tile.tres", GameAssetKind.Other)]
    [InlineData("Fonts/webfont.woff2", GameAssetKind.Other)]
    public void Classify_maps_a_bundle_extension_to_its_kind(string path, GameAssetKind expected)
        => AssetClassifier.Classify(path).Should().Be(expected);

    [Theory]
    [InlineData("README")]
    [InlineData("Models/model.unknownext")]
    [InlineData("trailing.")]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_returns_unknown_for_anything_it_does_not_recognize(string? path)
        => AssetClassifier.Classify(path).Should().Be(GameAssetKind.Unknown);

    [Theory]
    [InlineData(GameAssetKind.Image, "png", true)]
    [InlineData(GameAssetKind.SpriteAtlas, "xml", true)]
    [InlineData(GameAssetKind.Audio, "ogg", true)]
    [InlineData(GameAssetKind.Font, "ttf", true)]
    [InlineData(GameAssetKind.Vector, "svg", true)]
    [InlineData(GameAssetKind.TiledMap, "tmx", true)]
    [InlineData(GameAssetKind.Model3D, "glb", true)]
    [InlineData(GameAssetKind.Model3D, "gltf", true)]
    [InlineData(GameAssetKind.Model3D, "GLB", true)]
    [InlineData(GameAssetKind.Model3D, "fbx", false)]
    [InlineData(GameAssetKind.Model3D, "obj", false)]
    [InlineData(GameAssetKind.Model3D, "mtl", false)]
    [InlineData(GameAssetKind.Model3D, "dae", false)]
    [InlineData(GameAssetKind.Model3D, "stl", false)]
    [InlineData(GameAssetKind.Document, "txt", false)]
    [InlineData(GameAssetKind.Archive, "zip", false)]
    [InlineData(GameAssetKind.Other, "swf", false)]
    [InlineData(GameAssetKind.Unknown, "", false)]
    public void IsMaterializable_admits_only_what_the_engine_can_build(
        GameAssetKind kind, string extension, bool expected)
        => AssetClassifier.IsMaterializable(kind, extension).Should().Be(expected);

    [Fact]
    public void Classify_never_returns_sprite_atlas_because_that_takes_reading_the_document()
        => AssetClassifier.Classify("Spritesheet/spritesheet_default.xml")
            .Should().NotBe(GameAssetKind.SpriteAtlas);
}
