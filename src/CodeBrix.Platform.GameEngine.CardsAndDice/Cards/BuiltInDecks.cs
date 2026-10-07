using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Platform.GameEngine.CardsAndDice.Assets;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Cards;

/// <summary>Bundled deck definitions; each call to a deck factory creates independent physical cards.</summary>
public static class BuiltInDecks
{
    /// <summary>Creates a standard deck, with optional jokers and repeated packs.</summary>
    public static Deck PlayingCards(bool jokers = false, int copies = 1, int? seed = null, string theme = "traditional", string back = "backs/royal.svg")
    {
        if (theme is not ("traditional" or "simple")) throw new ArgumentException("Theme must be traditional or simple.", nameof(theme));
        var cards = new List<CardDefinition>();
        foreach (var (suit, name) in new[] { ("S", "Spades"), ("H", "Hearts"), ("D", "Diamonds"), ("C", "Clubs") })
            for (int value = 1; value <= 13; value++)
            {
                string rank = value switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => value.ToString() };
                cards.Add(new(rank + suit, rank + " of " + name, $"playing/{theme}/{rank}{suit}.svg", back: back, value: value));
            }
        if (jokers) for (int i = 1; i <= 2; i++) cards.Add(new("joker" + i, "Joker", $"playing/simple/joker{i}.svg", back: back, value: 0));
        return new(cards, "Playing cards", copies, seed);
    }
    /// <summary>Creates the 78-card historical tarot deck. Values are illustrative game values, not divinatory meanings.</summary>
    public static Deck Tarot(int? seed = null, string back = "backs/celestial.svg")
    {
        var assets = AssetCatalog.All.Where(a => a.Category == "tarot").ToArray();
        if (assets.Length != 78) throw new InvalidOperationException("The embedded tarot catalog must contain 78 cards.");
        return new(assets.Select((a, i) => new CardDefinition(a.Key, a.Name, a.Key, back, i % 13 + 1)), "Tarot", seed: seed);
    }
}
