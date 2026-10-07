using System;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using CodeBrix.Platform.GameEngine.CardsAndDice.Assets;
using CodeBrix.Platform.GameEngine.Drawing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Tests;

public class AssetCatalogTests
{
    [Fact]
    public void Every_embedded_asset_matches_its_provenance_hash_and_has_a_permissive_license()
    {
        AssetCatalog.All.Count.Should().BeGreaterThan(500);
        AssetCatalog.All.Select(a => a.Key).Should().OnlyHaveUniqueItems();
        var resources = typeof(AssetCatalog).Assembly.GetManifestResourceNames()
            .Where(n => n != "cardsdice/catalog.json").Select(n => n["cardsdice/".Length..]).Order();
        resources.Should().Equal(AssetCatalog.All.Select(a => a.Key).Order());
        foreach (var asset in AssetCatalog.All)
        {
            new[] { "MIT", "CC0-1.0", "Public-Domain" }.Should().Contain(asset.License);
            asset.Source.Should().NotBeEmpty();
            using var stream = AssetCatalog.Open(asset.Key);
            Convert.ToHexStringLower(SHA256.HashData(stream)).Should().Be(asset.Sha256);
        }
    }

    [Fact]
    public void All_svg_assets_are_self_contained_vector_art_and_render_nonempty()
    {
        foreach (var asset in AssetCatalog.All.Where(a => a.Key.EndsWith(".svg")))
        {
            var xml = XDocument.Parse(AssetCatalog.ReadSvg(asset.Key));
            xml.Descendants().Should().NotContain(e => e.Name.LocalName == "image" || e.Name.LocalName == "script" || e.Name.LocalName == "foreignObject");
            xml.Descendants().Attributes().Should().NotContain(a => a.Name.LocalName == "href" && !a.Value.StartsWith("#"));
            using var stream = AssetCatalog.Open(asset.Key);
            using var svg = SvgResource.Load(stream);
            var bitmap = svg.Rasterize(80, 120);
            bitmap.Pixels.Any(p => p.Alpha > 0).Should().BeTrue(asset.Key + " rendered empty");
        }
    }
}
