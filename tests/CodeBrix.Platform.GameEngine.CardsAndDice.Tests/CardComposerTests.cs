using System.IO;
using System.Text;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using CodeBrix.Platform.GameEngine.Drawing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Tests;

public class CardComposerTests
{
    [Fact]
    public void Composer_escapes_content_and_loads_as_an_svg()
    {
        var text = CardComposer.Compose("Fire & <Ice>", "symbols/original/fire.svg", 8, "A custom card");
        text.Should().Contain("&amp;");
        text.Should().NotContain("<image");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var svg = SvgResource.Load(stream);
        svg.IntrinsicSize.Width.Should().Be(250);
        svg.IntrinsicSize.Height.Should().Be(400);
    }
}
