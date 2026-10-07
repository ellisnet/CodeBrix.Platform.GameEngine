namespace CodeBrix.Platform.GameEngine.CardsAndDice.Layout;

/// <summary>Built-in ways to arrange cards.</summary>
public enum CardLayout
{
    /// <summary>A compact top-first stack.</summary>
    Stack,
    /// <summary>A horizontal row that overlaps to fit.</summary>
    Row,
    /// <summary>A curved, overlapping hand.</summary>
    Fan,
    /// <summary>A regular grid.</summary>
    Grid,
    /// <summary>A circle of cards.</summary>
    Circle,
    /// <summary>Positions supplied by the game.</summary>
    Manual
}
