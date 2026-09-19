using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CodeBrix.Platform.GameEngine.Assets.Providers;
using CodeBrix.Platform.GameEngine.KenneyAssets.Sources;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Tests;

/// <summary>
/// An OPT-IN scan of a whole Kenney "all in one" collection folder, which is far too large to keep in
/// the repository. It is skipped unless the environment variable <c>KENNEY_ALLIN1_DIR</c> points at such
/// a folder, and that variable is the only place the path comes from.
/// </summary>
/// <remarks>
/// <para>
/// What it holds: that registering hundreds of packs at once raises nothing, that every sprite atlas in
/// the corpus parses, that every tile map either parses or is refused with a message saying why, that
/// every audio asset carries an extension the engine has a reader for, and that no two assets of a pack
/// end up sharing a key.
/// </para>
/// <para>
/// It only CATALOGS. Nothing is materialized, so the thousands of model files in such a collection are
/// never opened - which is also what keeps the scan to seconds rather than hours.
/// </para>
/// </remarks>
public class KenneyAllInOneCorpusScan
{
    private const string FolderVariableName = "KENNEY_ALLIN1_DIR";

    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "ogg", "wav", "mp3", "flac" };

    [Fact]
    public void Every_pack_of_an_all_in_one_collection_catalogs_cleanly()
    {
        //Arrange
        string? folder = Environment.GetEnvironmentVariable(FolderVariableName);

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Assert.Skip(
                $"Set the environment variable {FolderVariableName} to a Kenney \"all in one\" " +
                "collection folder to run this scan. It is skipped by default because the corpus is far " +
                "too large to keep in the repository." +
                (string.IsNullOrWhiteSpace(folder)
                    ? string.Empty
                    : $" The folder '{folder}' does not exist."));
            return;
        }

        //Act - the whole collection as one registration, which is what a game would do
        Stopwatch stopwatch = Stopwatch.StartNew();
        using KenneyGameAssetProvider provider = new(new KenneyAssetsOptions
        {
            Sources = [folder],
            RecursiveFolders = true,
        });
        TimeSpan registered = stopwatch.Elapsed;

        IReadOnlyList<GameAssetDescriptor> assets = provider.Describe();
        TimeSpan described = stopwatch.Elapsed;

        //Assert - something was found at all
        provider.Packs.Should().NotBeEmpty();
        assets.Should().NotBeEmpty();
        assets.Count.Should().Be(provider.AssetCount);

        Report(provider, assets, registered, described);

        //Assert - a pack slug identifies exactly one pack, so a key identifies exactly one asset
        provider.Packs.Select(pack => pack.Slug).Should().OnlyHaveUniqueItems();
        assets.Select(asset => asset.Key.ToLowerInvariant()).Should().OnlyHaveUniqueItems();

        foreach (KenneyPackSummary pack in provider.Packs)
        {
            IReadOnlyList<GameAssetDescriptor> ofPack =
                provider.Describe(new GameAssetQuery { Pack = pack.Slug });

            ofPack.Count.Should().Be(pack.AssetCount);
            ofPack.Select(asset => asset.Path.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
        }

        //Assert - every sprite atlas parsed, and knows the sheet image it cuts frames from
        foreach (GameAssetDescriptor atlas in
            assets.Where(asset => asset.Kind == GameAssetKind.SpriteAtlas))
        {
            atlas.Properties.Should().ContainKey(KenneyAssetProperties.AtlasFrameCount);
            int.Parse(atlas.Properties[KenneyAssetProperties.AtlasFrameCount], CultureInfo.InvariantCulture)
                .Should().BeGreaterThan(0, $"'{atlas.Key}' should hold frames");
            atlas.Properties[KenneyAssetProperties.AtlasImagePath].Should()
                .NotBeNullOrWhiteSpace($"'{atlas.Key}' should name its sheet image");
        }

        //Assert - every tile map either parsed or says why it could not
        foreach (GameAssetDescriptor map in assets.Where(asset => asset.Kind == GameAssetKind.TiledMap))
        {
            bool parsed = map.Properties.ContainsKey(KenneyAssetProperties.MapWidth);

            if (parsed)
            {
                map.Properties.Should().ContainKey(KenneyAssetProperties.TileLayerCount);
                map.Properties.Should().ContainKey(KenneyAssetProperties.TilesetCount);
                continue;
            }

            map.Properties.Should().ContainKey(
                KenneyAssetProperties.TiledMapError,
                $"'{map.Key}' did not parse, so it should carry the reason");
            map.Properties[KenneyAssetProperties.TiledMapError].Should()
                .NotBeNullOrWhiteSpace($"'{map.Key}' should say why it did not parse");
        }

        //Assert - every audio asset is in a format the engine has a reader for
        foreach (GameAssetDescriptor audio in assets.Where(asset => asset.Kind == GameAssetKind.Audio))
        {
            audio.Properties.Should().ContainKey(KenneyAssetProperties.Extension);
            AudioExtensions.Should().Contain(
                audio.Properties[KenneyAssetProperties.Extension],
                $"'{audio.Key}' should be readable");
        }

        //Assert - every asset names the provider, its pack and whether it can be materialized
        assets.Should().AllSatisfy(asset =>
        {
            asset.ProviderId.Should().Be(provider.ProviderId);
            asset.Pack.Should().NotBeNullOrWhiteSpace();
            asset.Properties.Should().ContainKey(KenneyAssetProperties.Materializable);
        });
    }

    private static void Report(
        KenneyGameAssetProvider provider,
        IReadOnlyList<GameAssetDescriptor> assets,
        TimeSpan registered,
        TimeSpan described)
    {
        ITestOutputHelper? output = TestContext.Current.TestOutputHelper;

        if (output is null) { return; }

        output.WriteLine($"Packs registered: {provider.Packs.Count}");
        output.WriteLine($"Assets addressable: {assets.Count}");
        output.WriteLine($"Registration took: {registered.TotalSeconds:F2} s");
        output.WriteLine($"Describe took a further: {(described - registered).TotalSeconds:F2} s");

        foreach (IGrouping<GameAssetKind, GameAssetDescriptor> group in assets
            .GroupBy(asset => asset.Kind)
            .OrderBy(group => (int)group.Key))
        {
            int materializable = group.Count(asset =>
                asset.Properties.TryGetValue(KenneyAssetProperties.Materializable, out string? value)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

            output.WriteLine(
                $"  {group.Key}: {group.Count()} ({materializable} materializable)");
        }

        int unparsedMaps = assets.Count(asset =>
            asset.Kind == GameAssetKind.TiledMap
            && !asset.Properties.ContainsKey(KenneyAssetProperties.MapWidth));
        output.WriteLine($"Tile maps that did not parse: {unparsedMaps}");

        IReadOnlyList<string> warnings = provider.Warnings;
        output.WriteLine($"Warnings: {warnings.Count}");

        foreach (string warning in warnings.Take(40)) { output.WriteLine($"  {warning}"); }

        if (warnings.Count > 40)
        {
            output.WriteLine($"  ... and {warnings.Count - 40} more");
        }
    }
}
