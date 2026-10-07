using System;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Cards;

/// <summary>A physical card. Its definition can be shared; its identity and state cannot.</summary>
public sealed class Card
{
    internal Card(CardDefinition definition, Deck deck) { Definition = definition; Deck = deck; }
    /// <summary>Unique identity of this physical copy.</summary>
    public Guid Id { get; } = Guid.NewGuid();
    /// <summary>Shared immutable definition.</summary>
    public CardDefinition Definition { get; }
    /// <summary>Deck that created this instance.</summary>
    public Deck Deck { get; }
    /// <summary>Current pile, or null while drawn and not placed.</summary>
    public CardPile? Pile { get; internal set; }
    /// <summary>Whether the front is visible.</summary>
    public bool IsFaceUp { get; set; }
    /// <summary>Rotation in degrees, independently of facing.</summary>
    public float Rotation { get; set; }
    /// <summary>Game selection, for keeping or replacing cards.</summary>
    public bool IsSelected { get; set; }
}
