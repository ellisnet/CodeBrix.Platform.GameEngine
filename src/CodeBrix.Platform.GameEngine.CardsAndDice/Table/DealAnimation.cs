using System;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

/// <summary>Timing and presentation for a sequential deal.</summary>
public sealed record DealAnimation
{
    /// <summary>Travel style.</summary>
    public DealStyle Style { get; init; } = DealStyle.Arc;
    /// <summary>Travel seconds per card before applying AnimationSpeed.</summary>
    public double Duration { get; init; } = .45;
    /// <summary>Pause between cards, in seconds before applying AnimationSpeed.</summary>
    public double Interval { get; init; } = .08;
    /// <summary>Maximum upward lift in table coordinates.</summary>
    public float ArcHeight { get; init; } = 60;
    internal void Validate()
    {
        if (!Enum.IsDefined(Style) || !double.IsFinite(Duration) || Duration <= 0 ||
            !double.IsFinite(Interval) || Interval < 0 || !float.IsFinite(ArcHeight) || ArcHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(DealAnimation));
    }
}
