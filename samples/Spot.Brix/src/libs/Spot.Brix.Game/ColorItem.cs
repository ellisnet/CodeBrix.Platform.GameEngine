using SkiaSharp;

namespace Spot.Brix;

/// <summary>
/// A named player colour: the fill used for the player's spots and score panel, and the text colour
/// that reads against it.
/// </summary>
public class ColorItem
{
    /// <summary>Gets the display name of the colour, as shown in the New Game dialog.</summary>
    public string Name { get; }

    /// <summary>Gets the fill colour used for the player's spots and score panel.</summary>
    public SKColor Color { get; }

    /// <summary>Gets the colour used for text drawn on top of <see cref="Color"/>.</summary>
    public SKColor TextColor { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorItem"/> class.
    /// </summary>
    /// <param name="name">The display name of the colour.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="textColor">The colour of text drawn on the fill.</param>
    public ColorItem(string name, SKColor color, SKColor textColor)
    {
        Name = name;
        Color = color;
        TextColor = textColor;
    }
}
