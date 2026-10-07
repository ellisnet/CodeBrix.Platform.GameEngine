namespace CodeBrix.Platform.GameEngine.CardsAndDice.Dice;

/// <summary>One equally likely face, with a label, numeric value and optional SVG artwork.</summary>
public sealed record DieFace(string Label, int Value, string? Artwork = null);
