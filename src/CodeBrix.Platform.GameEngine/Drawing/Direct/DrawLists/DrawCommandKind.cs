namespace CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists; //CodeBrix (not from Gondwana)

/// <summary>What a <see cref="DrawCommand"/> draws.</summary>
public enum DrawCommandKind
{
    /// <summary>A picture fitted into a box centred on a point.</summary>
    Image = 0,

    /// <summary>A rectangle centred on a point, filled and/or outlined, optionally with rounded corners.</summary>
    Rectangle = 1,

    /// <summary>A circle centred on a point, filled and/or outlined.</summary>
    Circle = 2,

    /// <summary>One line of text, anchored at a point.</summary>
    Text = 3,
}
