using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

public sealed partial class CardsAndDiceTable
{
    private readonly List<ArtworkPreparation> _preparations = [];
    /// <summary>
    /// Starts non-blocking preparation of both card sides. Call on the engine thread;
    /// Update installs completed images and advances progress. Wait for successful
    /// completion before starting animation. Clear/Dispose cancel outstanding work.
    /// </summary>
    public ArtworkPreparation BeginPrepareCards(IEnumerable<Card> cards)
    {
        Check(); ArgumentNullException.ThrowIfNull(cards);
        var keys = cards.SelectMany(c => new[] { c.Definition.Back, c.Definition.Face })
            .Distinct(StringComparer.Ordinal).Where(k => !_images.ContainsKey(k)).ToArray();
        var preparation = new ArtworkPreparation(keys.Select(k => (k, _custom.GetValueOrDefault(k))));
        if (!preparation.IsComplete) _preparations.Add(preparation);
        return preparation;
    }
    private void CancelPreparation()
    {
        foreach (var preparation in _preparations) preparation.Cancel();
        _preparations.Clear();
    }
}
