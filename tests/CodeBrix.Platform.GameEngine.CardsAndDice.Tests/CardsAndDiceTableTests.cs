using System.Linq;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using CodeBrix.Platform.GameEngine.CardsAndDice.Dice;
using CodeBrix.Platform.GameEngine.CardsAndDice.Layout;
using CodeBrix.Platform.GameEngine.CardsAndDice.Table;
using CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Tests;

public class CardsAndDiceTableTests
{
    [Fact]
    public void Final_shuffle_top_matches_the_deck_even_with_different_card_backs()
    {
        using var table = new CardsAndDiceTable();
        var deck = new Deck(new[] {
            new CardDefinition("a", "A", "playing/simple/AS.svg", "backs/royal.svg"),
            new CardDefinition("b", "B", "playing/simple/2S.svg", "backs/crimson.svg") });
        table.AddArea(deck.DrawPile, new(40, 200, 220, 510), CardLayout.Stack);
        table.Shuffle(deck.DrawPile);
        table.Update(CardsAndDiceTable.ShuffleDurationSeconds - .01);
        var list = new DrawList();
        table.Draw(list);
        list.Commands.Last(c => c.Kind == DrawCommandKind.Image).Image
            .Should().BeSameAs(table.Image(deck.DrawPile.Peek()!.Definition.Back));
    }

    [Fact]
    public void Final_riffle_visibly_replaces_the_left_top_card_with_the_right_top_card()
    {
        using var table = new CardsAndDiceTable();
        var deck = BuiltInDecks.PlayingCards();
        table.AddArea(deck.DrawPile, new(40, 200, 220, 510), CardLayout.Stack);
        table.Shuffle(deck.DrawPile);
        table.Update(1);
        var list = new DrawList();
        table.Draw(list);
        list.Commands.Last(c => c.Kind == DrawCommandKind.Image).Rotation.Should().BeLessThan(0);
        table.Update(1.25);
        list.Clear();
        table.Draw(list);
        list.Commands.Last(c => c.Kind == DrawCommandKind.Image).Rotation.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(.5f)]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Shuffle_uses_clip_duration_and_separate_packets_without_losing_cards(float speed)
    {
        using var table = new CardsAndDiceTable { AnimationSpeed = speed };
        var deck = BuiltInDecks.PlayingCards();
        table.AddArea(deck.DrawPile, new(40, 200, 220, 510), CardLayout.Stack);
        table.Shuffle(deck.DrawPile);
        var order = deck.DrawPile.Cards.Select(c => c.Id).ToArray();
        table.Update(1);
        var list = new DrawList();
        table.Draw(list);
        var cards = list.Commands.Where(c => c.Kind == DrawCommandKind.Image).ToArray();
        (cards.Max(c => c.X) - cards.Min(c => c.X)).Should().BeGreaterThan(100);
        cards.Should().Contain(c => c.Rotation < 0);
        cards.Should().Contain(c => c.Rotation > 0);
        table.Update(2);
        table.IsAnimating.Should().BeTrue();
        table.Update(CardsAndDiceTable.ShuffleDurationSeconds - 3 + .001);
        table.IsAnimating.Should().BeFalse();
        deck.DrawPile.Cards.Select(c => c.Id).Should().Equal(order);
    }

    [Fact]
    public void Reduced_motion_shuffle_completes_immediately()
    {
        using var table = new CardsAndDiceTable { ReducedMotion = true };
        var deck = BuiltInDecks.PlayingCards();
        table.AddArea(deck.DrawPile, new(0, 0, 200, 300), CardLayout.Stack);
        table.Shuffle(deck.DrawPile);
        table.IsAnimating.Should().BeFalse();
    }

    [Fact]
    public void Dice_labels_share_the_face_center()
    {
        using var table = new CardsAndDiceTable();
        table.AddDie(new Die(20), new(210, 180));
        table.Roll();
        table.CompleteAnimations();
        var list = new DrawList();
        table.Draw(list);
        var label = list.Commands.Should().ContainSingle(c => c.Kind == DrawCommandKind.Text).Which;
        label.X.Should().Be(210);
        label.Y.Should().Be(180);
    }

    [Fact]
    public void Animation_timing_does_not_change_rolls_and_held_dice_remain_fixed()
    {
        using var a = new CardsAndDiceTable(77);
        using var b = new CardsAndDiceTable(77) { ReducedMotion = true };
        var ad = new Die();
        var bd = new Die();
        a.AddDie(ad, new(50, 50));
        b.AddDie(bd, new(50, 50));
        for (int i = 0; i < 25; i++)
        {
            a.Roll();
            a.Update(.13);
            a.Update(1.7);
            b.Roll();
            bd.Result.Should().Be(ad.Result);
        }
        var held = ad.Result;
        ad.IsHeld = true;
        a.Roll();
        ad.Result.Should().Be(held);
    }

    [Fact]
    public void Rejected_drops_are_atomic_and_swap_displace_rules_conserve_cards()
    {
        var deck = BuiltInDecks.PlayingCards();
        var hand = new CardPile("hand");
        var slot = new CardPile("slot");
        var discard = new CardPile("displaced");
        var first = deck.Draw(hand)!;
        var second = deck.Draw(slot)!;
        using var table = new CardsAndDiceTable();
        table.AddArea(hand, new(0, 0, 200, 200));
        var area = table.AddArea(slot, new(250, 0, 450, 200));
        area.SingleCard = true;
        table.TryDrop(first, area).Should().BeFalse();
        first.Pile.Should().BeSameAs(hand);
        second.Pile.Should().BeSameAs(slot);
        area.OccupiedRule = OccupiedDropRule.Swap;
        table.TryDrop(first, area).Should().BeTrue();
        second.Pile.Should().BeSameAs(hand);
        area.OccupiedRule = OccupiedDropRule.Displace;
        area.DisplacementPile = discard;
        table.TryDrop(second, area).Should().BeTrue();
        first.Pile.Should().BeSameAs(discard);
    }

    [Fact]
    public void Flip_and_rotation_are_independent_and_completion_fires_once()
    {
        var deck = BuiltInDecks.PlayingCards();
        using var table = new CardsAndDiceTable();
        table.AddArea(deck.DrawPile, new(0, 0, 200, 200));
        var card = deck.DrawPile.Peek()!;
        int count = 0;
        table.AnimationsCompleted += () => count++;
        table.Flip(card);
        table.Rotate(card);
        table.Update(.1);
        card.IsFaceUp.Should().BeTrue();
        card.Rotation.Should().Be(180);
        table.IsAnimating.Should().BeTrue();
        table.CompleteAnimations();
        table.Update(10);
        table.IsAnimating.Should().BeFalse();
        count.Should().Be(1);
    }

    [Fact]
    public void Card_commands_are_centered_and_drag_cancel_preserves_location()
    {
        var deck = BuiltInDecks.PlayingCards();
        var hand = new CardPile("hand");
        var card = deck.Draw(hand)!;
        using var table = new CardsAndDiceTable();
        table.AddArea(hand, new(100, 100, 300, 300));
        var list = new DrawList();
        table.Draw(list);
        var image = list.Commands.Should().ContainSingle(c => c.Kind == DrawCommandKind.Image).Which;
        image.X.Should().Be(200);
        image.Y.Should().Be(200);
        table.Pointer(new(200, 200), pressed: true).Should().BeTrue();
        table.Pointer(new(500, 500));
        table.CancelDrag();
        table.CompleteAnimations();
        card.Pile.Should().BeSameAs(hand);
        list.Clear();
        table.Draw(list);
        list.Commands.Should().ContainSingle(c => c.Kind == DrawCommandKind.Image).Which.X.Should().Be(200);
    }

    [Fact]
    public void Modal_input_cannot_move_or_select_underlying_cards()
    {
        var deck = BuiltInDecks.PlayingCards();
        using var table = new CardsAndDiceTable();
        table.AddArea(deck.DrawPile, new(0, 0, 200, 200));
        table.Draw(new DrawList());
        int clicks = 0;
        table.CardClicked += _ => clicks++;
        table.InputEnabled = false;
        table.Pointer(new(100, 100), pressed: true).Should().BeFalse();
        table.Pointer(new(100, 100), released: true);
        clicks.Should().Be(0);
        deck.DrawPile.Count.Should().Be(52);
    }
}
