using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Cards;

/// <summary>A developer-defined pack, including all its cards wherever they currently reside.</summary>
public sealed class Deck
{
    /// <summary>Creates physical copies of the supplied definitions without automatically shuffling.</summary>
    public Deck(IEnumerable<CardDefinition> definitions, string name = "Deck", int copies = 1, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (copies < 1) throw new ArgumentOutOfRangeException(nameof(copies));
        var items = definitions.ToArray();
        if (items.Any(d => d is null)) throw new ArgumentException("Definitions cannot contain null.", nameof(definitions));
        DrawPile = new(name); DiscardPile = new(name + " discards"); Random = seed.HasValue ? new(seed.Value) : new();
        var cards = new List<Card>();
        for (int copy = 0; copy < copies; copy++) foreach (var d in items) { var card = new Card(d, this); cards.Add(card); DrawPile.Add(card); }
        Cards = cards.AsReadOnly();
    }
    /// <summary>All physical cards owned by this deck.</summary>
    public IReadOnlyList<Card> Cards { get; }
    /// <summary>Available cards.</summary>
    public CardPile DrawPile { get; }
    /// <summary>Discarded cards.</summary>
    public CardPile DiscardPile { get; }
    /// <summary>Random source used only for logical operations.</summary>
    public Random Random { get; }
    /// <summary>Shuffles only the draw pile.</summary>
    public void Shuffle() => DrawPile.Shuffle(Random);
    /// <summary>Draws into an optional hand, choosing its facing.</summary>
    public Card? Draw(CardPile? hand = null, bool faceUp = true)
    {
        var card = DrawPile.Draw(); if (card is null) return null;
        card.IsFaceUp = faceUp; hand?.Add(card); return card;
    }
    /// <summary>Deals up to count cards without repeating any physical card.</summary>
    public IReadOnlyList<Card> Deal(int count, CardPile hand, bool faceUp = true)
    {
        ArgumentNullException.ThrowIfNull(hand);
        if (ReferenceEquals(hand, DrawPile)) throw new ArgumentException("Cannot deal back into the draw pile.", nameof(hand));
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var result = new List<Card>();
        for (int i = 0; i < count && Draw(hand, faceUp) is { } card; i++) result.Add(card);
        return result.AsReadOnly();
    }
    /// <summary>Returns every owned card from any location. Foreign cards in mixed piles are untouched.</summary>
    public void Reset(bool shuffle = true)
    {
        foreach (var card in Cards) { DrawPile.Add(card); card.IsFaceUp = false; card.IsSelected = false; card.Rotation = 0; }
        if (shuffle) Shuffle();
    }
    /// <summary>Discards one of this deck's physical cards.</summary>
    public void Discard(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!ReferenceEquals(card.Deck, this)) throw new ArgumentException("Card belongs to another deck.", nameof(card));
        DiscardPile.Add(card);
    }
}
