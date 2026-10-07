using System;
using System.Linq;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

public sealed partial class CardsAndDiceTable
{
    /// <summary>Opt-in automatic selection; None preserves game-owned click behavior.</summary>
    public CardSelectionMode SelectionMode { get; set; }
    /// <summary>Optional restriction on automatic selection, such as cards in the current player's hand.</summary>
    public Func<Card, bool>? CanSelect { get; set; }
    /// <summary>Optional per-card drag restriction. DragEnabled is the master switch.</summary>
    public Func<Card, bool>? CanDrag { get; set; }
    private void SelectOnClick(Card card)
    {
        if (SelectionMode == CardSelectionMode.None || CanSelect?.Invoke(card) == false) return;
        bool selected = !card.IsSelected;
        if (SelectionMode == CardSelectionMode.Single)
            foreach (var item in _areas.SelectMany(a => a.Pile.Cards)) item.IsSelected = false;
        card.IsSelected = selected;
    }
}
