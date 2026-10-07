using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Layout;

/// <summary>Pure layout calculations, independent of rendering and the engine.</summary>
public static class CardLayouts
{
    /// <summary>Returns poses inside a rectangle. Cards remain their requested size.</summary>
    public static IReadOnlyList<CardPose> Arrange(CardLayout layout, int count, float x, float y, float width, float height,
        float cardWidth = 110, float cardHeight = 168)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (!Enum.IsDefined(layout) || !float.IsFinite(x) || !float.IsFinite(y) ||
            !float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0 ||
            !float.IsFinite(cardWidth) || !float.IsFinite(cardHeight) || cardWidth <= 0 || cardHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        var poses = new CardPose[count];
        int columns = Math.Max(1, (int)(width / (cardWidth + 10)));
        for (int i = 0; i < count; i++)
        {
            float mid = (count - 1) / 2f;
            float step = count <= 1 ? 0 : Math.Min(cardWidth + 12, Math.Max(0, width - cardWidth) / (count - 1));
            float px = x + width / 2 + (i - mid) * step, py = y + height / 2, angle = 0;
            switch (layout)
            {
                case CardLayout.Stack: px = x + width / 2 + Math.Min(i, 3) * 2; py -= Math.Min(i, 3) * 2; break;
                case CardLayout.Fan:
                    angle = count <= 1 ? 0 : (i - mid) * Math.Min(5, 32f / Math.Max(1, count - 1));
                    py += Math.Abs(angle) * 1.2f; break;
                case CardLayout.Grid: px = x + cardWidth / 2 + i % columns * (cardWidth + 10); py = y + cardHeight / 2 + i / columns * (cardHeight + 10); break;
                case CardLayout.Circle:
                    double a = i * Math.PI * 2 / Math.Max(1, count) - Math.PI / 2;
                    px = x + width / 2 + (float)Math.Cos(a) * Math.Max(0, (width - cardWidth) / 2);
                    py = y + height / 2 + (float)Math.Sin(a) * Math.Max(0, (height - cardHeight) / 2); break;
            }
            poses[i] = new(new(px, py), angle);
        }
        return Array.AsReadOnly(poses);
    }
}
