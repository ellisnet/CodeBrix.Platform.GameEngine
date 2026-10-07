using System.Collections.Generic;
using System.Collections.ObjectModel;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

/// <summary>A queued deal's progress. Cards are recorded in actual top-of-deck launch order.</summary>
public sealed class DealSequence
{
    private readonly List<Card> _cards = [];
    private readonly ReadOnlyCollection<Card> _view;
    internal DealSequence(int remaining) { Remaining = remaining; _view = _cards.AsReadOnly(); }
    internal int Remaining;
    internal void Launched(Card card) => _cards.Add(card);
    /// <summary>Cards actually drawn so far; an exhausted deck can yield fewer than requested.</summary>
    public IReadOnlyList<Card> Cards => _view;
    /// <summary>True after all requested transfers finish, the deck runs out, or cancellation.</summary>
    public bool IsComplete => Remaining == 0;
    /// <summary>True if cleared, disposed or explicitly canceled before completion.</summary>
    public bool IsCanceled { get; internal set; }
}
