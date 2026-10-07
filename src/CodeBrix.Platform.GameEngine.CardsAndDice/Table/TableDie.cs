using System.Numerics;
using CodeBrix.Platform.GameEngine.CardsAndDice.Dice;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

/// <summary>A die's table placement.</summary>
public sealed class TableDie
{
    internal TableDie(Die die, Vector2 center, float size) { Die = die; Center = center; Size = size; }
    /// <summary>The logical die.</summary>
    public Die Die { get; }
    /// <summary>Center in table coordinates.</summary>
    public Vector2 Center { get; set; }
    /// <summary>Drawing size in pixels.</summary>
    public float Size { get; set; }
    internal double RollLeft;
}
