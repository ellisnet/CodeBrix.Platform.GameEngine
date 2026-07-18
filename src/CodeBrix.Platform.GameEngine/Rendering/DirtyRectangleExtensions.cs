using System.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Rendering; //was previously: Gondwana.Rendering;
internal static class DirtyRectangleExtensions
{
    internal static void AddDeduped(this List<Rectangle> rects, Rectangle rect)
    {
        // If an existing rect fully contains this one, skip it
        for (int i = 0; i < rects.Count; i++)
        {
            if (rects[i].Contains(rect))
                return;
        }

        // Merge with any overlapping rects
        for (int i = rects.Count - 1; i >= 0; i--)
        {
            if (rect.IntersectsWith(rects[i]))
            {
                rect = Rectangle.Union(rect, rects[i]);
                rects.RemoveAt(i);
            }
        }

        rects.Add(rect);
    }
}
