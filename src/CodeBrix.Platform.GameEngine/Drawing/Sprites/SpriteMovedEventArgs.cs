using System.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Sprites; //was previously: Gondwana.Drawing.Sprites;
/// <summary>
/// Provides data for sprite movement events.
/// </summary>
public class SpriteMovedEventArgs : EventArgs
{
    /// <summary>
    /// The sprite that was moved.
    /// </summary>
    public Sprite sprite;

    /// <summary>
    /// The previous position of the sprite before it was moved.
    /// </summary>
    public PointF oldPt;

    /// <summary>
    /// The new position of the sprite after it was moved.
    /// </summary>
    public PointF newPt;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteMovedEventArgs"/> class.
    /// </summary>
    /// <param name="_sprite">The sprite that was moved.</param>
    /// <param name="_oldPt">The previous position of the sprite.</param>
    /// <param name="_newPt">The new position of the sprite.</param>
    protected internal SpriteMovedEventArgs(Sprite _sprite, PointF _oldPt, PointF _newPt)
    {
        sprite = _sprite;
        oldPt = _oldPt;
        newPt = _newPt;
    }
}