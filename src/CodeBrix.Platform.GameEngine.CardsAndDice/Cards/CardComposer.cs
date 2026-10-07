using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using CodeBrix.Platform.GameEngine.CardsAndDice.Assets;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Cards;

/// <summary>Composes self-contained SVG card faces using embedded glyphs and customizable colors.</summary>
public static class CardComposer
{
    /// <summary>Creates a card with title, central symbol, description and value badge. Text is XML-escaped.</summary>
    public static string Compose(string title, string symbolKey, int value, string description = "",
        string background = "#182638", string accent = "#dfbb70")
    {
        ArgumentNullException.ThrowIfNull(title); ArgumentNullException.ThrowIfNull(description);
        if (!global::SkiaSharp.SKColor.TryParse(background, out _) || !global::SkiaSharp.SKColor.TryParse(accent, out _))
            throw new ArgumentException("Colors must be valid CSS hex colors.");
        XNamespace ns = "http://www.w3.org/2000/svg";
        var symbol = XElement.Parse(AssetCatalog.ReadSvg(symbolKey));
        symbol.SetAttributeValue("x", 55); symbol.SetAttributeValue("y", 115);
        symbol.SetAttributeValue("width", 140); symbol.SetAttributeValue("height", 140);
        // Monochrome glyphs take the card's accent while preserving explicit 'none'.
        foreach (var e in symbol.DescendantsAndSelf())
            foreach (var attr in e.Attributes().Where(a => a.Name.LocalName is "fill" or "stroke").ToArray())
                if (attr.Value != "none") attr.Value = accent;
        XElement text(string t, int x, int y, int size) => new(ns + "text", new XAttribute("x", x), new XAttribute("y", y),
            new XAttribute("text-anchor", "middle"), new XAttribute("font-family", "sans-serif"), new XAttribute("font-size", size), new XAttribute("fill", accent), t);
        var root = new XElement(ns + "svg", new XAttribute("viewBox", "0 0 250 400"), new XAttribute("width", 250), new XAttribute("height", 400),
            new XElement(ns + "rect", new XAttribute("x", 2), new XAttribute("y", 2), new XAttribute("width", 246), new XAttribute("height", 396), new XAttribute("rx", 16), new XAttribute("fill", background), new XAttribute("stroke", accent), new XAttribute("stroke-width", 3)),
            new XElement(ns + "rect", new XAttribute("x", 13), new XAttribute("y", 13), new XAttribute("width", 224), new XAttribute("height", 374), new XAttribute("rx", 10), new XAttribute("fill", "none"), new XAttribute("stroke", accent)),
            text(title, 125, 57, 28), symbol, text(value.ToString(CultureInfo.InvariantCulture), 125, 310, 48));
        int line = 0;
        foreach (var part in Wrap(description, 25).Take(3)) root.Add(text(part, 125, 339 + line++ * 17, 12));
        return root.ToString(SaveOptions.DisableFormatting);
    }
    private static IEnumerable<string> Wrap(string text, int length)
    {
        string line = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        { if (line.Length + word.Length + 1 > length && line.Length > 0) { yield return line; line = ""; } line += (line.Length == 0 ? "" : " ") + word; }
        if (line.Length > 0) yield return line;
    }
}
