using CodeBrix.Platform.GameEngine.CardsAndDice.Assets;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Tests;

public class BuiltInDecksTests
{
    [Theory]
    [InlineData("traditional")]
    [InlineData("simple")]
    [InlineData("tarot")]
    public void Complete_decks_resolve_every_face_and_back(string theme)
    {
        var deck = theme == "tarot" ? BuiltInDecks.Tarot() : BuiltInDecks.PlayingCards(true, theme: theme);
        foreach (var card in deck.Cards)
        {
            using var face = AssetCatalog.Open(card.Definition.Face);
            using var back = AssetCatalog.Open(card.Definition.Back);
        }
        deck.Cards.Count.Should().Be(theme == "tarot" ? 78 : 54);
    }

    [Fact]
    public void Custom_backs_apply_to_every_card_including_jokers_and_tarot()
    {
        foreach (var deck in new[] { BuiltInDecks.PlayingCards(jokers: true, copies: 2, back: "custom/back"), BuiltInDecks.Tarot(back: "custom/back") })
            deck.Cards.Should().AllSatisfy(card => card.Definition.Back.Should().Be("custom/back"));
        BuiltInDecks.PlayingCards().Cards[0].Definition.Back.Should().Be("backs/royal.svg");
        BuiltInDecks.Tarot().Cards[0].Definition.Back.Should().Be("backs/celestial.svg");
    }
}
