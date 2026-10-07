using System;
using System.Linq;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Tests;

public class CardPileTests
{
    [Fact]
    public void Random_transfers_never_duplicate_or_lose_cards()
    {
        var deck = BuiltInDecks.PlayingCards(jokers: true);
        CardPile[] piles = [deck.DrawPile, deck.DiscardPile, new("hand"), new("table")];
        var rng = new Random(725);
        for (int i = 0; i < 3000; i++)
        {
            var card = deck.Cards[rng.Next(deck.Cards.Count)];
            piles[rng.Next(piles.Length)].Add(card, rng.Next(2) == 0);
            if (i % 17 == 0) piles[rng.Next(piles.Length)].Shuffle(rng);
            var all = piles.SelectMany(p => p.Cards).ToArray();
            all.Length.Should().Be(54);
            all.Select(c => c.Id).Distinct().Count().Should().Be(54);
            // Plain loops rather than AllSatisfy: a pile may legitimately be empty.
            foreach (var pile in piles)
                foreach (var c in pile.Cards) c.Pile.Should().BeSameAs(pile);
        }
    }
}
