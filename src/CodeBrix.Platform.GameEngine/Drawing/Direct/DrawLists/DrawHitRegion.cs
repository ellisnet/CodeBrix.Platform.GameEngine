namespace CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists; //CodeBrix (not from Gondwana)

/// <summary>
/// A rectangle on a draw list that a pointer can hit, for example a button or a link. It draws nothing; it is
/// published with the commands so a pointer test reads the same frame the player sees.
/// </summary>
/// <param name="X">The centre X, in the same coordinates as the list's commands.</param>
/// <param name="Y">The centre Y, in the same coordinates as the list's commands.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Id">What the region stands for (a button name, a link, ...).</param>
public readonly record struct DrawHitRegion(float X, float Y, float Width, float Height, string Id)
{
    /// <summary>Gets a value indicating whether a point is inside the region (edges included).</summary>
    /// <param name="x">The point's X, in the list's coordinates.</param>
    /// <param name="y">The point's Y, in the list's coordinates.</param>
    /// <returns><see langword="true"/> when the point is inside the region.</returns>
    public bool Contains(double x, double y) =>
        x >= X - (Width / 2) && x <= X + (Width / 2) && y >= Y - (Height / 2) && y <= Y + (Height / 2);
}
