using System;
using System.Linq;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Tests;

public class DeckTests
{
    [Fact]
    public void Multiple_packs_keep_unique_physical_identity_and_reset_only_their_own_cards()
    {
        var a = BuiltInDecks.PlayingCards(copies: 3, seed: 123);
        var b = BuiltInDecks.PlayingCards(seed: 42);
        var hand = new CardPile("mixed");
        a.Deal(130, hand);
        b.Deal(7, hand);
        a.Reset(false);
        a.Cards.Select(c => c.Id).Distinct().Count().Should().Be(156);
        a.DrawPile.Count.Should().Be(156);
        hand.Count.Should().Be(7);
        hand.Cards.Should().AllSatisfy(c => c.Deck.Should().BeSameAs(b));
    }

    [Fact]
    public void Empty_draw_cut_and_invalid_deal_are_well_defined()
    {
        var deck = BuiltInDecks.PlayingCards();
        Action dealIntoDrawPile = () => deck.Deal(1, deck.DrawPile);
        dealIntoDrawPile.Should().Throw<ArgumentException>();
        var order = deck.DrawPile.Cards.ToArray();
        deck.DrawPile.Cut(13);
        deck.DrawPile.Peek().Should().BeSameAs(order[13]);
        var hand = new CardPile("hand");
        deck.Deal(100, hand).Count.Should().Be(52);
        deck.Draw().Should().BeNull();
        deck.DrawPile.Peek().Should().BeNull();
    }

    [Fact]
    public void Seeded_shuffle_is_repeatable_and_does_not_reintroduce_drawn_cards()
    {
        var a = BuiltInDecks.PlayingCards(seed: 5);
        var b = BuiltInDecks.PlayingCards(seed: 5);
        a.Shuffle();
        b.Shuffle();
        a.DrawPile.Cards.Select(c => c.Definition.Key).Should().Equal(b.DrawPile.Cards.Select(c => c.Definition.Key));
        var card = a.Draw();
        a.Shuffle();
        a.DrawPile.Cards.Should().NotContain(card!);
    }
}
