namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

/// <summary>Reusable card travel styles. Motion never changes the deck's random state.</summary>
public enum DealStyle
{
    /// <summary>Slide directly to the destination.</summary>
    Slide,
    /// <summary>Lift along an arc and settle at the destination.</summary>
    Arc,
    /// <summary>Arc with one full turn before landing.</summary>
    Toss
}
