using System.Numerics;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Layout;

/// <summary>One card's center and clockwise rotation.</summary>
public readonly record struct CardPose(Vector2 Center, float Rotation = 0);
