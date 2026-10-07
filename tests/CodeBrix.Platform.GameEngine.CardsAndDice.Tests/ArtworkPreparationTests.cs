using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using CodeBrix.Platform.GameEngine.CardsAndDice.Layout;
using CodeBrix.Platform.GameEngine.CardsAndDice.Table;
using CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Tests;

public class ArtworkPreparationTests
{
    private static int CachedImages(CardsAndDiceTable table) => table._images.Count;

    [Fact]
    public async Task Background_preparation_reports_progress_without_dealing_and_reuses_cache()
    {
        using var table = new CardsAndDiceTable();
        var deck = BuiltInDecks.PlayingCards(theme: "simple");
        var load = table.BeginPrepareCards(deck.Cards.Take(3));
        load.Total.Should().Be(4);
        load.IsComplete.Should().BeFalse();
        for (int i = 0; i < 2000 && !load.IsComplete; i++)
        {
            table.Update(.01);
            load.Progress.Should().BeInRange(0, 1);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        load.IsComplete.Should().BeTrue();
        load.Error.Should().BeNull();
        load.Progress.Should().Be(1);
        deck.DrawPile.Count.Should().Be(52);
        table.IsAnimating.Should().BeFalse();
        CachedImages(table).Should().Be(4);
        var again = table.BeginPrepareCards(deck.Cards.Take(3));
        again.IsComplete.Should().BeTrue();
        again.Total.Should().Be(0);
    }

    [Fact]
    public async Task Clear_cancels_loading_and_failures_are_reported_without_throwing_in_update()
    {
        using var table = new CardsAndDiceTable();
        var load = table.BeginPrepareCards(BuiltInDecks.Tarot().Cards);
        table.Clear();
        load.IsCanceled.Should().BeTrue();
        load.IsComplete.Should().BeTrue();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        table.Update(0);
        CachedImages(table).Should().Be(0);
        var bad = new Deck(new[] { new CardDefinition("bad", "Bad", "missing.svg", "missing.svg") });
        var failure = table.BeginPrepareCards(bad.Cards);
        for (int i = 0; i < 200 && !failure.IsComplete; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
            table.Update(0);
        }
        failure.IsComplete.Should().BeTrue();
        failure.Error.Should().NotBeNull();
        var abandoned = table.BeginPrepareCards(BuiltInDecks.PlayingCards().Cards);
        table.Dispose();
        abandoned.IsCanceled.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dealing_never_loads_new_images_during_travel_or_reveal(bool queued)
    {
        using var table = new CardsAndDiceTable();
        var deck = BuiltInDecks.PlayingCards();
        table.AddArea(deck.DrawPile, new(0, 0, 200, 300), CardLayout.Stack);
        var hand = table.AddArea(new CardPile("Hand"), new(250, 0, 650, 300));
        if (queued) table.DealRoundRobin(deck, new[] { hand }, 3);
        else table.Deal(deck, hand.Pile, 3);
        int prepared = CachedImages(table);
        prepared.Should().BeGreaterThanOrEqualTo(4);
        var draw = new DrawList();
        for (int i = 0; i < 300; i++)
        {
            table.Update(.01);
            draw.Clear();
            table.Draw(draw);
            CachedImages(table).Should().Be(prepared);
        }
        table.IsAnimating.Should().BeFalse();
    }

    [Fact]
    public void Setup_preparation_reuses_custom_art_and_survives_table_clear()
    {
        using var table = new CardsAndDiceTable();
        table.RegisterSvg("element", CardComposer.Compose("Fire", "symbols/original/fire.svg", 3));
        CachedImages(table).Should().Be(1);
        var deck = new Deck(new[] { new CardDefinition("fire", "Fire", "element") }, copies: 4);
        table.PrepareCards(deck.Cards);
        CachedImages(table).Should().Be(2);
        var face = table.Image("element");
        var back = table.Image("backs/royal.svg");
        table.Clear();
        table.PrepareCards(deck.Cards);
        CachedImages(table).Should().Be(2);
        table.Image("element").Should().BeSameAs(face);
        table.Image("backs/royal.svg").Should().BeSameAs(back);
        deck.DrawPile.Count.Should().Be(4);
        deck.Cards.Should().AllSatisfy(card => card.IsFaceUp.Should().BeFalse());
    }
}
