using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Parsing;

/// <summary>
/// Parses Tiled map (<c>.tmx</c>) and tile set (<c>.tsx</c>) documents into
/// <see cref="TiledMapDocument"/> and <see cref="TiledTilesetDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// The parser produces documents and nothing else: it resolves no paths, reads no images and touches
/// no engine state. What it will not accept, it rejects with a message that names the feature and the
/// supported alternative - an infinite map, an orientation other than orthogonal, or layer data in an
/// encoding it cannot read. Supported layer encodings are CSV and base64, the latter uncompressed or
/// compressed with zlib or gzip, all of which the base class library decodes.
/// </para>
/// <para>
/// Features a document may carry that the parser keeps as data rather than rejecting: tile offsets,
/// tile set and per-tile properties, object layers, image layers, per-tile collision shapes and tile
/// sets that hold one image per tile. Whether any of those can be reproduced is the importer's
/// decision, not the parser's.
/// </para>
/// </remarks>
internal static class TiledMapParser
{
    private const string SupportedEncodings =
        "supported encodings are CSV and base64 (uncompressed, zlib or gzip)";

    /// <summary>
    /// Parses a Tiled map document.
    /// </summary>
    /// <param name="xmlText">The full text of the <c>.tmx</c> document.</param>
    /// <param name="documentPath">The archive path of the document, used in messages and carried on the result.</param>
    /// <returns>The parsed map.</returns>
    /// <exception cref="TiledMapParseException">Thrown when the text is not a Tiled map, or uses a feature that cannot be imported.</exception>
    public static TiledMapDocument ParseMap(string? xmlText, string? documentPath = null)
    {
        string label = MapLabel(documentPath);
        XElement root = ParseRoot(xmlText, "map", documentPath, label);

        string orientation = (string?)root.Attribute("orientation") ?? "orthogonal";
        if (!orientation.Equals("orthogonal", StringComparison.OrdinalIgnoreCase))
        {
            throw new TiledMapParseException(
                $"{label} uses the '{orientation}' orientation; only orthogonal maps can be imported.",
                documentPath);
        }

        if (string.Equals((string?)root.Attribute("infinite"), "1", StringComparison.Ordinal))
        {
            throw new TiledMapParseException(
                $"{label} is an infinite map; only finite maps can be imported.", documentPath);
        }

        int width = ReadRequiredInt(root, "width", label, documentPath);
        int height = ReadRequiredInt(root, "height", label, documentPath);
        int tileWidth = ReadRequiredInt(root, "tilewidth", label, documentPath);
        int tileHeight = ReadRequiredInt(root, "tileheight", label, documentPath);

        List<TiledTilesetReference> tilesets = [];
        List<TiledTileLayer> tileLayers = [];
        List<TiledObjectLayer> objectLayers = [];
        List<TiledImageLayer> imageLayers = [];
        List<string> warnings = [];
        int layerIndex = 0;

        foreach (XElement element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "tileset":
                    tilesets.Add(ParseTilesetReference(element, label, documentPath));
                    break;

                case "layer":
                    tileLayers.Add(ParseTileLayer(element, layerIndex++, width, height, label, documentPath));
                    break;

                case "objectgroup":
                    objectLayers.Add(ParseObjectLayer(element, layerIndex++));
                    break;

                case "imagelayer":
                    TiledImageLayer imageLayer = ParseImageLayer(element, layerIndex++);
                    imageLayers.Add(imageLayer);
                    warnings.Add(
                        $"Image layer '{imageLayer.Name}' is listed but cannot be imported: the engine has no " +
                        "per-layer background image.");
                    break;

                case "group":
                    warnings.Add(
                        $"Layer group '{(string?)element.Attribute("name") ?? string.Empty}' and the layers " +
                        "inside it were not imported: nested layer groups are not supported.");
                    break;

                default:
                    break;
            }
        }

        if (tilesets.Count == 0)
        {
            throw new TiledMapParseException($"{label} references no tile sets.", documentPath);
        }

        if (tileLayers.Count == 0)
        {
            throw new TiledMapParseException($"{label} holds no tile layers.", documentPath);
        }

        return new TiledMapDocument
        {
            DocumentPath = documentPath ?? string.Empty,
            Orientation = "orthogonal",
            RenderOrder = (string?)root.Attribute("renderorder") ?? string.Empty,
            Width = width,
            Height = height,
            TileWidth = tileWidth,
            TileHeight = tileHeight,
            Tilesets = tilesets,
            TileLayers = tileLayers,
            ObjectLayers = objectLayers,
            ImageLayers = imageLayers,
            Properties = ParseProperties(root),
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Attempts to parse a Tiled map document, handing back the rejection message instead of throwing.
    /// </summary>
    /// <param name="xmlText">The full text of the <c>.tmx</c> document.</param>
    /// <param name="documentPath">The archive path of the document, used in the message and carried on the result.</param>
    /// <param name="map">The parsed map when the method returns <see langword="true"/>.</param>
    /// <param name="error">The reason the document was rejected when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the document parsed; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// This is the form the pack index uses: one unreadable map in a bundle must not stop the rest of
    /// the bundle from being cataloged.
    /// </remarks>
    public static bool TryParseMap(
        string? xmlText,
        string? documentPath,
        [NotNullWhen(true)] out TiledMapDocument? map,
        [NotNullWhen(false)] out string? error)
    {
        try
        {
            map = ParseMap(xmlText, documentPath);
            error = null;
            return true;
        }
        catch (TiledMapParseException exception)
        {
            map = null;
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Parses a Tiled tile set document.
    /// </summary>
    /// <param name="xmlText">The full text of the <c>.tsx</c> document.</param>
    /// <param name="documentPath">The archive path of the document, used in messages and carried on the result.</param>
    /// <returns>The parsed tile set.</returns>
    /// <exception cref="TiledMapParseException">Thrown when the text is not a Tiled tile set, or lacks the geometry needed to cut tiles.</exception>
    public static TiledTilesetDocument ParseTileset(string? xmlText, string? documentPath = null)
    {
        XElement root = ParseRoot(xmlText, "tileset", documentPath, TilesetLabel(null, documentPath));
        return ParseTilesetElement(root, documentPath);
    }

    /// <summary>
    /// Attempts to parse a Tiled tile set document, handing back the rejection message instead of
    /// throwing.
    /// </summary>
    /// <param name="xmlText">The full text of the <c>.tsx</c> document.</param>
    /// <param name="documentPath">The archive path of the document, used in the message and carried on the result.</param>
    /// <param name="tileset">The parsed tile set when the method returns <see langword="true"/>.</param>
    /// <param name="error">The reason the document was rejected when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the document parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParseTileset(
        string? xmlText,
        string? documentPath,
        [NotNullWhen(true)] out TiledTilesetDocument? tileset,
        [NotNullWhen(false)] out string? error)
    {
        try
        {
            tileset = ParseTileset(xmlText, documentPath);
            error = null;
            return true;
        }
        catch (TiledMapParseException exception)
        {
            tileset = null;
            error = exception.Message;
            return false;
        }
    }

    private static TiledTilesetReference ParseTilesetReference(
        XElement element, string mapLabel, string? documentPath)
    {
        int firstGid = ReadOptionalInt(element, "firstgid") ?? 1;
        string? source = (string?)element.Attribute("source");
        if (!string.IsNullOrWhiteSpace(source))
        {
            return new TiledTilesetReference { FirstGid = firstGid, Source = source };
        }

        //An inline tile set is parsed with the map's own path: its image reference resolves against
        //  the map, because there is no separate tile set document to resolve against
        TiledTilesetDocument inline = ParseTilesetElement(element, documentPath);
        if (inline.TileWidth <= 0 || inline.TileHeight <= 0)
        {
            throw new TiledMapParseException(
                $"{mapLabel} declares an inline tile set with no tile size.", documentPath);
        }

        return new TiledTilesetReference { FirstGid = firstGid, Inline = inline };
    }

    private static TiledTilesetDocument ParseTilesetElement(XElement element, string? documentPath)
    {
        string name = (string?)element.Attribute("name") ?? string.Empty;
        string label = TilesetLabel(name, documentPath);

        int tileWidth = ReadRequiredInt(element, "tilewidth", label, documentPath);
        int tileHeight = ReadRequiredInt(element, "tileheight", label, documentPath);
        int tileCount = ReadOptionalInt(element, "tilecount") ?? 0;
        int columns = ReadOptionalInt(element, "columns") ?? 0;

        XElement? image = element.Elements().FirstOrDefault(e => e.Name.LocalName == "image");
        string imagePath = (string?)image?.Attribute("source") ?? string.Empty;

        List<TiledTilesetTile> tiles = [];
        foreach (XElement tile in element.Elements().Where(e => e.Name.LocalName == "tile"))
        {
            int? id = ReadOptionalInt(tile, "id");
            if (id is null) { continue; }

            XElement? tileImage = tile.Elements().FirstOrDefault(e => e.Name.LocalName == "image");
            tiles.Add(new TiledTilesetTile
            {
                Id = id.Value,
                Type = (string?)tile.Attribute("type") ?? (string?)tile.Attribute("class"),
                ImagePath = (string?)tileImage?.Attribute("source"),
                Properties = ParseProperties(tile),
                HasCollisionShapes = tile.Elements().Any(e => e.Name.LocalName == "objectgroup"),
            });
        }

        if (imagePath.Length == 0 && !tiles.Any(t => !string.IsNullOrEmpty(t.ImagePath)))
        {
            throw new TiledMapParseException($"{label} declares no image.", documentPath);
        }

        if (imagePath.Length > 0 && columns <= 0)
        {
            throw new TiledMapParseException(
                $"{label} declares no column count, so its tiles cannot be located in its image.",
                documentPath);
        }

        XElement? tileOffset = element.Elements().FirstOrDefault(e => e.Name.LocalName == "tileoffset");

        return new TiledTilesetDocument
        {
            Name = name,
            DocumentPath = documentPath ?? string.Empty,
            TileWidth = tileWidth,
            TileHeight = tileHeight,
            TileCount = tileCount,
            Columns = columns,
            ImagePath = imagePath,
            ImageWidth = ReadOptionalInt(image, "width") ?? 0,
            ImageHeight = ReadOptionalInt(image, "height") ?? 0,
            Spacing = ReadOptionalInt(element, "spacing") ?? 0,
            Margin = ReadOptionalInt(element, "margin") ?? 0,
            TileOffsetX = ReadOptionalInt(tileOffset, "x") ?? 0,
            TileOffsetY = ReadOptionalInt(tileOffset, "y") ?? 0,
            Properties = ParseProperties(element),
            Tiles = tiles,
        };
    }

    private static TiledTileLayer ParseTileLayer(
        XElement layer, int documentIndex, int mapWidth, int mapHeight, string mapLabel, string? documentPath)
    {
        string name = (string?)layer.Attribute("name") ?? string.Empty;
        int width = ReadOptionalInt(layer, "width") ?? mapWidth;
        int height = ReadOptionalInt(layer, "height") ?? mapHeight;
        if (width <= 0 || height <= 0)
        {
            throw new TiledMapParseException(
                $"{mapLabel} layer '{name}' declares an empty size of {width} by {height} tiles.",
                documentPath);
        }

        XElement? data = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "data");
        if (data is null)
        {
            throw new TiledMapParseException(
                $"{mapLabel} layer '{name}' carries no tile data.", documentPath);
        }

        if (data.Elements().Any(e => e.Name.LocalName == "chunk"))
        {
            throw new TiledMapParseException(
                $"{mapLabel} layer '{name}' stores its tiles in chunks, which only an infinite map does; " +
                "only finite maps can be imported.",
                documentPath);
        }

        string? encoding = (string?)data.Attribute("encoding");
        string? compression = (string?)data.Attribute("compression");
        int expectedCount = width * height;

        uint[] gids = encoding?.ToLowerInvariant() switch
        {
            "csv" => ParseCsvData(data.Value, expectedCount, mapLabel, name, documentPath),
            "base64" => ParseBase64Data(
                data.Value, compression, expectedCount, mapLabel, name, documentPath),
            null => throw new TiledMapParseException(
                $"{mapLabel} layer '{name}' stores its tiles as unencoded XML elements; " +
                $"{SupportedEncodings}.",
                documentPath),
            _ => throw new TiledMapParseException(
                $"{mapLabel} layer '{name}' stores its tiles with the '{encoding}' encoding; " +
                $"{SupportedEncodings}.",
                documentPath),
        };

        return new TiledTileLayer
        {
            Id = ReadOptionalInt(layer, "id") ?? 0,
            Name = name,
            DocumentIndex = documentIndex,
            Width = width,
            Height = height,
            Gids = gids,
            Visible = !string.Equals((string?)layer.Attribute("visible"), "0", StringComparison.Ordinal),
            Opacity = Math.Clamp(ReadOptionalFloat(layer, "opacity") ?? 1.0f, 0.0f, 1.0f),
            OffsetX = ReadOptionalFloat(layer, "offsetx") ?? 0.0f,
            OffsetY = ReadOptionalFloat(layer, "offsety") ?? 0.0f,
            ParallaxX = ReadOptionalFloat(layer, "parallaxx") ?? 1.0f,
            ParallaxY = ReadOptionalFloat(layer, "parallaxy") ?? 1.0f,
            Properties = ParseProperties(layer),
        };
    }

    private static TiledObjectLayer ParseObjectLayer(XElement layer, int documentIndex)
    {
        List<TiledMapObject> objects = [];
        foreach (XElement mapObject in layer.Elements().Where(e => e.Name.LocalName == "object"))
        {
            objects.Add(new TiledMapObject
            {
                Id = ReadOptionalInt(mapObject, "id") ?? 0,
                Name = (string?)mapObject.Attribute("name") ?? string.Empty,
                Type = (string?)mapObject.Attribute("type")
                    ?? (string?)mapObject.Attribute("class")
                    ?? string.Empty,
                X = ReadOptionalFloat(mapObject, "x") ?? 0.0f,
                Y = ReadOptionalFloat(mapObject, "y") ?? 0.0f,
                Width = ReadOptionalFloat(mapObject, "width") ?? 0.0f,
                Height = ReadOptionalFloat(mapObject, "height") ?? 0.0f,
                Rotation = ReadOptionalFloat(mapObject, "rotation") ?? 0.0f,
                Gid = ReadOptionalUInt(mapObject, "gid") ?? 0u,
                Visible = !string.Equals(
                    (string?)mapObject.Attribute("visible"), "0", StringComparison.Ordinal),
                Properties = ParseProperties(mapObject),
            });
        }

        return new TiledObjectLayer
        {
            Id = ReadOptionalInt(layer, "id") ?? 0,
            Name = (string?)layer.Attribute("name") ?? string.Empty,
            DocumentIndex = documentIndex,
            Visible = !string.Equals((string?)layer.Attribute("visible"), "0", StringComparison.Ordinal),
            Opacity = Math.Clamp(ReadOptionalFloat(layer, "opacity") ?? 1.0f, 0.0f, 1.0f),
            OffsetX = ReadOptionalFloat(layer, "offsetx") ?? 0.0f,
            OffsetY = ReadOptionalFloat(layer, "offsety") ?? 0.0f,
            Objects = objects,
            Properties = ParseProperties(layer),
        };
    }

    private static TiledImageLayer ParseImageLayer(XElement layer, int documentIndex)
    {
        XElement? image = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "image");

        return new TiledImageLayer
        {
            Id = ReadOptionalInt(layer, "id") ?? 0,
            Name = (string?)layer.Attribute("name") ?? string.Empty,
            DocumentIndex = documentIndex,
            ImagePath = (string?)image?.Attribute("source") ?? string.Empty,
            Visible = !string.Equals((string?)layer.Attribute("visible"), "0", StringComparison.Ordinal),
            OffsetX = ReadOptionalFloat(layer, "offsetx") ?? 0.0f,
            OffsetY = ReadOptionalFloat(layer, "offsety") ?? 0.0f,
            Properties = ParseProperties(layer),
        };
    }

    private static uint[] ParseCsvData(
        string text, int expectedCount, string mapLabel, string layerName, string? documentPath)
    {
        string[] cells = text.Split(
            ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (cells.Length != expectedCount)
        {
            throw new TiledMapParseException(
                $"{mapLabel} layer '{layerName}' holds {cells.Length} tile values but its size calls for " +
                $"{expectedCount}.",
                documentPath);
        }

        uint[] gids = new uint[expectedCount];
        for (int index = 0; index < cells.Length; index++)
        {
            if (!uint.TryParse(
                cells[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out gids[index]))
            {
                throw new TiledMapParseException(
                    $"{mapLabel} layer '{layerName}' holds the tile value '{cells[index]}', which is not a " +
                    "global tile id.",
                    documentPath);
            }
        }

        return gids;
    }

    private static uint[] ParseBase64Data(
        string text,
        string? compression,
        int expectedCount,
        string mapLabel,
        string layerName,
        string? documentPath)
    {
        byte[] encoded;
        try
        {
            encoded = Convert.FromBase64String(text.Trim());
        }
        catch (FormatException exception)
        {
            throw new TiledMapParseException(
                $"{mapLabel} layer '{layerName}' holds tile data that is not valid base64.",
                documentPath,
                exception);
        }

        byte[] data;
        try
        {
            data = (compression ?? string.Empty).ToLowerInvariant() switch
            {
                "" => encoded,
                "zlib" => Decompress(new ZLibStream(new MemoryStream(encoded), CompressionMode.Decompress)),
                "gzip" => Decompress(new GZipStream(new MemoryStream(encoded), CompressionMode.Decompress)),
                _ => throw new TiledMapParseException(
                    $"{mapLabel} layer '{layerName}' compresses its tile data with '{compression}'; " +
                    $"{SupportedEncodings}.",
                    documentPath),
            };
        }
        catch (InvalidDataException exception)
        {
            throw new TiledMapParseException(
                $"{mapLabel} layer '{layerName}' holds tile data that could not be decompressed.",
                documentPath,
                exception);
        }

        if (data.Length != expectedCount * 4)
        {
            throw new TiledMapParseException(
                $"{mapLabel} layer '{layerName}' holds {data.Length} bytes of tile data but its size calls " +
                $"for {expectedCount * 4}.",
                documentPath);
        }

        uint[] gids = new uint[expectedCount];
        for (int index = 0; index < expectedCount; index++)
        {
            gids[index] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index * 4, 4));
        }

        return gids;
    }

    private static byte[] Decompress(Stream stream)
    {
        using (stream)
        {
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    private static IReadOnlyDictionary<string, string> ParseProperties(XElement element)
    {
        XElement? properties = element.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "properties");
        if (properties is null) { return TiledProperties.Empty; }

        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (XElement property in properties.Elements().Where(e => e.Name.LocalName == "property"))
        {
            string? name = (string?)property.Attribute("name");
            if (string.IsNullOrEmpty(name)) { continue; }

            //A multi-line property value is the element's text rather than a value attribute
            result[name] = (string?)property.Attribute("value") ?? property.Value;
        }

        return result.Count == 0 ? TiledProperties.Empty : result;
    }

    private static XElement ParseRoot(
        string? xmlText, string expectedName, string? documentPath, string label)
    {
        if (string.IsNullOrWhiteSpace(xmlText))
        {
            throw new TiledMapParseException($"{label} is empty.", documentPath);
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xmlText);
        }
        catch (XmlException exception)
        {
            throw new TiledMapParseException(
                $"{label} is not valid XML: {exception.Message}", documentPath, exception);
        }

        XElement? root = document.Root;
        if (root is null || root.Name.LocalName != expectedName)
        {
            throw new TiledMapParseException(
                $"{label} is not a Tiled document: its root element is " +
                $"'{root?.Name.LocalName ?? string.Empty}' rather than '{expectedName}'.",
                documentPath);
        }

        return root;
    }

    private static int ReadRequiredInt(
        XElement element, string attributeName, string label, string? documentPath)
    {
        int? value = ReadOptionalInt(element, attributeName);
        if (value is null or <= 0)
        {
            throw new TiledMapParseException(
                $"{label} declares no usable '{attributeName}' value.", documentPath);
        }

        return value.Value;
    }

    private static int? ReadOptionalInt(XElement? element, string attributeName)
    {
        string? text = (string?)element?.Attribute(attributeName);
        return text is not null
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : null;
    }

    private static uint? ReadOptionalUInt(XElement? element, string attributeName)
    {
        string? text = (string?)element?.Attribute(attributeName);
        return text is not null
            && uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value)
                ? value
                : null;
    }

    private static float? ReadOptionalFloat(XElement? element, string attributeName)
    {
        string? text = (string?)element?.Attribute(attributeName);
        return text is not null
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : null;
    }

    private static string MapLabel(string? documentPath) =>
        string.IsNullOrEmpty(documentPath) ? "The Tiled map" : $"Tiled map '{documentPath}'";

    private static string TilesetLabel(string? name, string? documentPath)
    {
        if (!string.IsNullOrEmpty(documentPath)) { return $"Tiled tile set '{documentPath}'"; }

        return string.IsNullOrEmpty(name) ? "The Tiled tile set" : $"Tiled tile set '{name}'";
    }
}
