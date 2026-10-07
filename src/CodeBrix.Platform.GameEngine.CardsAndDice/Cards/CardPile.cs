using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Cards;

/// <summary>An ordered hand, stack, discard pile, or table area. Index zero is the top.</summary>
public sealed class CardPile
{
    private readonly List<Card> _cards = [];
    private readonly ReadOnlyCollection<Card> _view;
    /// <summary>Creates an empty named pile.</summary>
    public CardPile(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); Name = name; _view = _cards.AsReadOnly();
    }
    /// <summary>Pile name.</summary>
    public string Name { get; }
    /// <summary>Cards in top-first order. Mutation goes through pile operations.</summary>
    public IReadOnlyList<Card> Cards => _view;
    /// <summary>Number of cards.</summary>
    public int Count => _cards.Count;
    /// <summary>Moves a card here atomically, removing it from its previous pile.</summary>
    public void Add(Card card, bool onTop = false)
    {
        ArgumentNullException.ThrowIfNull(card);
        card.Pile?._cards.Remove(card);
        if (onTop) _cards.Insert(0, card); else _cards.Add(card);
        card.Pile = this;
    }
    /// <summary>Looks at the top card, returning null when empty.</summary>
    public Card? Peek() => _cards.Count == 0 ? null : _cards[0];
    /// <summary>Removes and returns the top card, or null when empty.</summary>
    public Card? Draw()
    {
        var card = Peek(); if (card is null) return null;
        _cards.RemoveAt(0); card.Pile = null; return card;
    }
    /// <summary>Shuffles this pile with Fisher–Yates and the supplied random source.</summary>
    public void Shuffle(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        for (int i = Count - 1; i > 0; i--) { int j = random.Next(i + 1); (_cards[i], _cards[j]) = (_cards[j], _cards[i]); }
    }
    /// <summary>Moves the first count cards under the rest, preserving both groups' order.</summary>
    public void Cut(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > Count) throw new ArgumentOutOfRangeException(nameof(count));
        var first = _cards.GetRange(0, count); _cards.RemoveRange(0, count); _cards.AddRange(first);
    }
    /// <summary>Moves every card here from another pile, preserving order.</summary>
    public void Collect(CardPile source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(source, this)) return;
        foreach (var card in source.Cards.ToArray()) Add(card);
    }
}
