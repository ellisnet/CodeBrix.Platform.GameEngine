using System;
using System.Linq;
using System.Numerics;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using CodeBrix.Platform.GameEngine.CardsAndDice.Dice;
using CodeBrix.Platform.GameEngine.CardsAndDice.Layout;
using CodeBrix.Platform.GameEngine.CardsAndDice.Table;
using CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Tests;

public class CardsAndDiceTableDealingTests
{
    private static (CardsAndDiceTable Table, Deck Deck, TableArea A, TableArea B) Setup()
    {
        var table = new CardsAndDiceTable();
        var deck = BuiltInDecks.PlayingCards();
        table.AddArea(deck.DrawPile, new(0, 0, 200, 300), CardLayout.Stack);
        var a = table.AddArea(new CardPile("A"), new(250, 0, 450, 300), CardLayout.Manual);
        var b = table.AddArea(new CardPile("B"), new(500, 0, 700, 300), CardLayout.Manual);
        return (table, deck, a, b);
    }

    [Fact]
    public void Arrival_flips_through_edge_before_notifying_and_hidden_round_waits_for_click()
    {
        var (table, deck, a, _) = Setup();
        using var cleanup = table;
        int arrived = 0;
        table.CardDealt += (_, _) => arrived++;
        var first = table.DealTo(deck, a, new(new(350, 150)));
        var middle = table.DealTo(deck, a, new(new(350, 150)), faceUp: false);
        var third = table.DealTo(deck, a, new(new(350, 150)));
        table.Update(.45);
        var card = first.Cards.Should().ContainSingle().Which;
        var list = new DrawList();
        table.Draw(list);
        list.Commands.Last(c => c.Kind == DrawCommandKind.Image).Image.Should().BeSameAs(table.Image(card.Definition.Back));
        first.IsComplete.Should().BeFalse();
        arrived.Should().Be(0);
        table.Update(.14);
        list.Clear();
        table.Draw(list);
        list.Commands.Last(c => c.Kind == DrawCommandKind.Image).Width.Should().BeLessThan(a.CardWidth * .2f);
        table.Update(.02);
        list.Clear();
        table.Draw(list);
        list.Commands.Last(c => c.Kind == DrawCommandKind.Image).Image.Should().BeSameAs(table.Image(card.Definition.Face));
        first.IsComplete.Should().BeFalse();
        middle.Cards.Should().BeEmpty();
        table.CompleteAnimations();
        arrived.Should().Be(3);
        first.Cards[0].IsFaceUp.Should().BeTrue();
        middle.Cards[0].IsFaceUp.Should().BeFalse();
        third.Cards[0].IsFaceUp.Should().BeTrue();
        // Put the hidden card in a free spot, then exercise the same click-to-reveal handler as the sample.
        var hidden = middle.Cards[0];
        a.ManualPoses[hidden.Id] = new(new(420, 260));
        table.CompleteAnimations();
        table.Draw(new DrawList());
        table.CardClicked += c => { if (!c.IsFaceUp) table.Flip(c); };
        table.Pointer(new(420, 260), pressed: true);
        table.Pointer(new(420, 260), released: true);
        hidden.IsFaceUp.Should().BeTrue();
        table.IsAnimating.Should().BeTrue();
        table.Update(.15);
        list.Clear();
        table.Draw(list);
        list.Commands.Should().Contain(c => c.Kind == DrawCommandKind.Image && c.Width < a.CardWidth * .03);
        table.CompleteAnimations();
        table.IsAnimating.Should().BeFalse();
    }

    [Fact]
    public void Traditional_dice_animate_pips_and_settle_on_the_logical_face()
    {
        using var table = new CardsAndDiceTable(seed: 5);
        var die = Die.Traditional();
        table.AddDie(die, new(250, 250));
        die.Faces.Select(f => f.Value).Should().Equal(Enumerable.Range(1, 6));
        table.Roll();
        var result = die.Result;
        table.Update(.2);
        var list = new DrawList();
        table.Draw(list);
        list.Commands.Should().Contain(c => c.Kind == DrawCommandKind.Image && c.Rotation != 0);
        table.CompleteAnimations();
        list.Clear();
        table.Draw(list);
        var face = list.Commands.Should().ContainSingle(c => c.Kind == DrawCommandKind.Image).Which;
        face.Image.Should().BeSameAs(table.Image(result!.Artwork!));
        face.Rotation.Should().Be(0);
        die.Result.Should().BeSameAs(result);
    }

    [Fact]
    public void Round_robin_draws_top_cards_in_turn_and_waits_for_shuffle()
    {
        var (table, deck, a, b) = Setup();
        using var cleanup = table;
        table.Shuffle(deck.DrawPile);
        var order = deck.DrawPile.Cards.Take(6).ToArray();
        var sequence = table.DealRoundRobin(deck, new[] { a, b }, 3);
        table.Update(1);
        sequence.Cards.Should().BeEmpty();
        deck.DrawPile.Count.Should().Be(52);
        table.Update(2.1);
        sequence.Cards.Should().ContainSingle();
        a.Pile.Cards[0].Should().BeSameAs(order[0]);
        order[0].IsFaceUp.Should().BeFalse();
        b.Pile.Cards.Should().BeEmpty();
        table.CompleteAnimations();
        sequence.IsComplete.Should().BeTrue();
        sequence.IsCanceled.Should().BeFalse();
        sequence.Cards.Should().Equal(order);
        a.Pile.Cards.Should().Equal(order[0], order[2], order[4]);
        b.Pile.Cards.Should().Equal(order[1], order[3], order[5]);
        sequence.Cards.Should().AllSatisfy(c => c.IsFaceUp.Should().BeTrue());
    }

    [Theory]
    [InlineData(DealStyle.Slide)]
    [InlineData(DealStyle.Arc)]
    [InlineData(DealStyle.Toss)]
    public void Arbitrary_pose_lands_exactly_and_notifies_once(DealStyle style)
    {
        var (table, deck, a, _) = Setup();
        using var cleanup = table;
        int arrived = 0;
        table.CardDealt += (_, target) =>
        {
            target.Should().BeSameAs(a);
            arrived++;
        };
        var sequence = table.DealTo(deck, a, new(new(380, 210), 37), false,
            new DealAnimation { Style = style, Duration = 1 });
        table.Update(.5);
        sequence.IsComplete.Should().BeFalse();
        arrived.Should().Be(0);
        table.Update(.5);
        sequence.IsComplete.Should().BeTrue();
        arrived.Should().Be(1);
        table.Update(5);
        arrived.Should().Be(1);
        sequence.Cards[0].IsFaceUp.Should().BeFalse();
        var list = new DrawList();
        table.Draw(list);
        var landing = list.Commands.Should().ContainSingle(c => c.Kind == DrawCommandKind.Image && Math.Abs(c.Rotation - 37) < .001).Which;
        landing.X.Should().Be(380);
        landing.Y.Should().Be(210);
    }

    [Fact]
    public void Exhaustion_and_cancel_preserve_physical_cards()
    {
        var (table, deck, a, b) = Setup();
        using var cleanup = table;
        var sequence = table.DealRoundRobin(deck, new[] { a, b }, 40);
        table.CompleteAnimations();
        sequence.IsComplete.Should().BeTrue();
        sequence.Cards.Count.Should().Be(52);
        deck.Reset(false);
        var cancel = table.DealRoundRobin(deck, new[] { a, b }, 4);
        table.Update(.1);
        table.CancelDeals();
        cancel.IsComplete.Should().BeTrue();
        cancel.IsCanceled.Should().BeTrue();
        cancel.Cards.Should().ContainSingle();
        deck.DrawPile.Count.Should().Be(51);
        a.Pile.Count.Should().Be(1);
        table.IsDealing.Should().BeFalse();
    }

    [Fact]
    public void Validation_is_atomic_and_clear_cancels_queued_work()
    {
        var (table, deck, a, _) = Setup();
        using var cleanup = table;
        using var other = new CardsAndDiceTable();
        var foreign = other.AddArea(new CardPile("foreign"), new(0, 0, 100, 100));
        Action dealToForeignArea = () => table.DealRoundRobin(deck, new[] { a, foreign }, 2);
        dealToForeignArea.Should().Throw<ArgumentException>();
        table.IsDealing.Should().BeFalse();
        deck.DrawPile.Count.Should().Be(52);
        var sequence = table.DealTo(deck, a);
        table.Clear();
        sequence.IsCanceled.Should().BeTrue();
        deck.DrawPile.Count.Should().Be(52);
    }

    [Fact]
    public void Large_and_small_updates_deal_the_same_order()
    {
        var (one, d1, a1, b1) = Setup();
        using var x = one;
        var (two, d2, a2, b2) = Setup();
        using var y = two;
        var s1 = one.DealRoundRobin(d1, new[] { a1, b1 }, 4);
        var s2 = two.DealRoundRobin(d2, new[] { a2, b2 }, 4);
        one.Update(8);
        for (int i = 0; i < 800; i++) two.Update(.01);
        s1.IsComplete.Should().BeTrue();
        s2.IsComplete.Should().BeTrue();
        s2.Cards.Select(c => c.Definition.Key).Should().Equal(s1.Cards.Select(c => c.Definition.Key));
        a2.Pile.Cards.Select(c => c.Definition.Key).Should().Equal(a1.Pile.Cards.Select(c => c.Definition.Key));
    }

    [Fact]
    public void Click_selection_and_manual_drag_use_no_game_event_handlers()
    {
        var (table, deck, a, _) = Setup();
        using var cleanup = table;
        table.SelectionMode = CardSelectionMode.Multiple;
        table.DealTo(deck, a, new(new(350, 150)));
        table.CompleteAnimations();
        table.Draw(new DrawList());
        table.Pointer(new(350, 150), pressed: true);
        table.Pointer(new(350, 150), released: true);
        var card = a.Pile.Cards.Should().ContainSingle().Which;
        card.IsSelected.Should().BeTrue();
        card.IsSelected = false;
        table.Draw(new DrawList());
        table.Pointer(new(350, 150), pressed: true);
        table.Pointer(new(400, 230));
        table.Pointer(new(400, 230), released: true);
        table.CompleteAnimations();
        a.ManualPoses[card.Id].Center.Should().Be(new Vector2(400, 230));
        card.Pile.Should().BeSameAs(a.Pile);
    }
}
