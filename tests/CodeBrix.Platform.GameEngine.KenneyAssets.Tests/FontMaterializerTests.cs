using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using CodeBrix.Platform.GameEngine.Rendering.Text;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// Validates what a game gets from materializing the fonts in a Kenney pack: a typeface registered with
/// the engine's font manager under the asset's key, ready to hand to a text element, from a zip bundle or
/// an extracted folder alike.
/// </summary>
/// <remarks>
/// Glyph SHAPES are never asserted - what matters is that the font data loaded, carries a family name and
/// maps the characters a game will draw with it.
/// </remarks>
public class FontMaterializerTests : IDisposable
{
    private const string ProviderId = KenneyPackIndex.DefaultProviderId;
    private const string SpaceFontPath = "Fonts/Kenney Space.ttf";
    private const string NarrowFontPath = "Fonts/Kenney Future Narrow.ttf";
    private const string TouchFontPath = "Fonts/kenney_input_touch.ttf";
    private const string BasicLatin = "ABCXYZabcxyz0123456789 .,!?";

    private readonly FontMaterializer _materializer = new();
    private readonly List<KenneyAssetSource> _sources = [];
    private readonly List<string> _scratchFolders = [];
    private readonly List<string> _keys = [];

    /// <summary>
    /// Removes only the font keys these tests registered - the font manager is shared - and closes the
    /// bundles and scratch folders they opened.
    /// </summary>
    public void Dispose()
    {
        foreach (string key in _keys) { FontManager.Instance.Remove(key); }
        foreach (KenneyAssetSource source in _sources) { source.Dispose(); }
        foreach (string folder in _scratchFolders) { TestFixtures.DeleteScratchFolder(folder); }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(SpaceFontPath, "Kenney Space")]
    [InlineData(NarrowFontPath, "Kenney Future Narrow")]
    [InlineData(TouchFontPath, "Kenney Input Touch")]
    public void Materialize_registers_the_typeface_under_the_key(string path, string expectedFamilyName)
    {
        //Arrange
        KenneyAssetEntry entry = FontEntry(TestFixtures.SimulatedBundleFileName, path);
        string key = Key(entry);

        //Act
        SKTypeface typeface = _materializer.Materialize(entry, key);

        //Assert - registered under the key the engine looks a font up by, and carrying real font data
        typeface.FamilyName.Should().Be(expectedFamilyName);
        typeface.GlyphCount.Should().BeGreaterThan(0);
        FontManager.Instance.Contains(key).Should().BeTrue();
        FontManager.Instance.Get(key).Should().BeSameAs(typeface);
        FontManager.Instance.GetOrDefault(key).Should().BeSameAs(typeface);
    }

    [Theory]
    [InlineData(SpaceFontPath)]
    [InlineData(NarrowFontPath)]
    public void Materialize_yields_a_typeface_that_can_draw_basic_latin(string path)
    {
        //Arrange
        KenneyAssetEntry entry = FontEntry(TestFixtures.SimulatedBundleFileName, path);

        //Act
        SKTypeface typeface = _materializer.Materialize(entry, Key(entry));
        ushort[] glyphs = typeface.GetGlyphs(BasicLatin);

        //Assert - glyph 0 is "no glyph for this character", so a text font maps every one of these
        glyphs.Length.Should().Be(BasicLatin.Length);
        glyphs.Any(glyph => glyph == 0).Should().BeFalse(
            $"'{typeface.FamilyName}' should map every character of \"{BasicLatin}\"");
    }

    [Fact]
    public void Materialize_loads_an_icon_font_that_maps_no_basic_latin()
    {
        //Arrange - the input-prompt fonts Kenney ships are icon fonts: they load, they carry glyphs, and
        //  their code points are not the letters a game would type
        KenneyAssetEntry entry = FontEntry(TestFixtures.SimulatedBundleFileName, TouchFontPath);

        //Act
        SKTypeface typeface = _materializer.Materialize(entry, Key(entry));
        ushort[] glyphs = typeface.GetGlyphs("ABCabc");

        //Assert
        typeface.GlyphCount.Should().BeGreaterThan(0);
        glyphs.All(glyph => glyph == 0).Should().BeTrue(
            $"'{typeface.FamilyName}' is an icon font and maps no basic Latin");
    }

    [Fact]
    public void Materialize_returns_the_typeface_already_registered_under_the_key()
    {
        //Arrange
        KenneyAssetEntry entry = FontEntry(TestFixtures.SimulatedBundleFileName, SpaceFontPath);
        string key = Key(entry);
        SKTypeface first = _materializer.Materialize(entry, key);

        //Act
        SKTypeface second = _materializer.Materialize(entry, key);
        SKTypeface fromAnotherMaterializer = new FontMaterializer().Materialize(entry, key);

        //Assert - re-registering a key disposes the typeface already under it, which text elements may
        //  still be drawing with, so the first materialization of a key wins
        second.Should().BeSameAs(first);
        fromAnotherMaterializer.Should().BeSameAs(first);
        FontManager.Instance.Get(key).Should().BeSameAs(first);
    }

    [Fact]
    public void Materialize_loads_an_asset_from_an_extracted_folder_like_its_zip()
    {
        //Arrange - the same font reached both ways; two keys, because one key holds one typeface
        KenneyAssetEntry fromZip = FontEntry(TestFixtures.SimulatedBundleFileName, NarrowFontPath);
        KenneyAssetEntry fromFolder = FolderFontEntry(TestFixtures.SimulatedBundleFileName, NarrowFontPath);

        //Act
        SKTypeface zipTypeface = _materializer.Materialize(fromZip, Key(fromZip, "zip"));
        SKTypeface folderTypeface = _materializer.Materialize(fromFolder, Key(fromFolder, "folder"));

        //Assert
        folderTypeface.Should().NotBeSameAs(zipTypeface);
        folderTypeface.FamilyName.Should().Be(zipTypeface.FamilyName);
        folderTypeface.GlyphCount.Should().Be(zipTypeface.GlyphCount);
    }

    [Fact]
    public void Materialize_never_opens_a_nested_web_font_archive()
    {
        //Arrange - Kenney's font packs place web-font archives beside the loose .ttf files that hold the
        //  same faces; a nested archive is catalogued as one and is not a font asset
        string bundlePath = BuildBundle("webfonts", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Webfont Pack", "1.0")),
            ["Fonts/Webfonts A.zip"] = [0x50, 0x4B, 0x03, 0x04],
        });
        KenneyAssetEntry entry = Entry(OpenSource(bundlePath), "Fonts/Webfonts A.zip");
        string key = Key(entry);

        //Act
        UnsupportedGameAssetException thrown =
            Assert.Throws<UnsupportedGameAssetException>(() => _materializer.Materialize(entry, key));

        //Assert
        entry.Kind.Should().Be(GameAssetKind.Archive);
        thrown.Kind.Should().Be(GameAssetKind.Archive);
        thrown.Key.Should().Be(key);
        thrown.Message.Should().StartWith(UnsupportedGameAssetException.DefaultMessage);
        FontManager.Instance.Contains(key).Should().BeFalse();
    }

    [Fact]
    public void Materialize_reports_the_asset_when_the_bytes_are_not_a_font()
    {
        //Arrange
        string bundlePath = BuildBundle("broken-font", new Dictionary<string, byte[]>
        {
            ["License.txt"] = Encoding.UTF8.GetBytes(TestFixtures.LicenseText("Broken Font", "1.0")),
            ["Fonts/broken.ttf"] = Encoding.UTF8.GetBytes("this is not a font file"),
        });
        KenneyAssetEntry entry = Entry(OpenSource(bundlePath), "Fonts/broken.ttf");
        string key = Key(entry);

        //Act
        Action act = () => _materializer.Materialize(entry, key);

        //Assert - the message names the asset, and a failed load leaves the key free
        InvalidDataException thrown = Assert.Throws<InvalidDataException>(act);
        thrown.Message.Should().Contain("Fonts/broken.ttf");
        thrown.Message.Should().Contain(key);
        thrown.InnerException.Should().NotBeNull();
        FontManager.Instance.Contains(key).Should().BeFalse();
    }

    [Fact]
    public void Materialize_throws_for_an_asset_that_is_not_a_font()
    {
        //Arrange
        KenneyAssetEntry image =
            Entry(OpenBundle(TestFixtures.SimulatedBundleFileName), "PNG/tile_0079.png");
        string key = Key(image);

        //Act
        UnsupportedGameAssetException thrown =
            Assert.Throws<UnsupportedGameAssetException>(() => _materializer.Materialize(image, key));

        //Assert
        thrown.Kind.Should().Be(GameAssetKind.Image);
        FontManager.Instance.Contains(key).Should().BeFalse();
    }

    [Fact]
    public void Materialize_rejects_a_missing_entry_and_a_blank_key()
    {
        //Arrange
        KenneyAssetEntry entry = FontEntry(TestFixtures.SimulatedBundleFileName, SpaceFontPath);

        //Act
        Action noEntry = () => _materializer.Materialize(null!, "kenney:pack/Fonts/font");
        Action blankKey = () => _materializer.Materialize(entry, " ");

        //Assert
        noEntry.Should().Throw<ArgumentNullException>();
        blankKey.Should().Throw<ArgumentException>();
    }

    //Every key a test asks for is also a key the fixture removes afterwards
    private string Key(KenneyAssetEntry entry) => Track(entry.GetKey(ProviderId));

    //A second key for the same asset, standing in for a caller's own registration key
    private string Key(KenneyAssetEntry entry, string suffix) => Track($"{entry.GetKey(ProviderId)}-{suffix}");

    private static KenneyAssetEntry Entry(KenneyAssetSource source, string path) =>
        source.Packs[0].Entries.Single(e => e.Path == path);

    private string Track(string key)
    {
        _keys.Add(key);
        return key;
    }

    private KenneyAssetEntry FontEntry(string bundleFileName, string path) =>
        Entry(OpenBundle(bundleFileName), path);

    private KenneyAssetEntry FolderFontEntry(string bundleFileName, string path)
    {
        string folder = TestFixtures.ExtractBundle(bundleFileName, "font-folder");
        _scratchFolders.Add(folder);
        return Entry(OpenSource(folder), path);
    }

    private KenneyAssetSource OpenBundle(string bundleFileName) =>
        OpenSource(TestFixtures.BundlePath(bundleFileName));

    private KenneyAssetSource OpenSource(string path)
    {
        KenneyAssetSource source = KenneyAssetSource.Open(path);
        _sources.Add(source);
        return source;
    }

    private string BuildBundle(string purpose, IReadOnlyDictionary<string, byte[]> entries)
    {
        string folder = TestFixtures.CreateScratchFolder(purpose);
        _scratchFolders.Add(folder);
        string zipPath = Path.Combine(folder, $"kenney_{purpose}.zip");
        TestFixtures.BuildZip(zipPath, entries);
        return zipPath;
    }
}
