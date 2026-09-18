using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.Drawing; //was previously: Gondwana.Drawing;

/// <summary>
/// One repeated image of a drawable on a wrapped <see cref="SceneLayer"/>: the canonical owner
/// paired with the world-space translation that places this image.
/// </summary>
/// <remarks>
/// A render instance, never a clone of the tile, sprite, drawing, or widget. While a scope is
/// entered, <see cref="Rendering.Views.View.WorldPxToScreenPx"/> adds the translation so the owner
/// paints at the repeated position without its own state being moved.
/// </remarks>
/// <param name="owner">The canonical drawable this instance repeats.</param>
/// <param name="layer">The wrapped layer supplying the repetition lattice.</param>
/// <param name="offset">The world-space translation of this image, in pixels.</param>
internal sealed class WrappedDrawable(IDrawable owner, SceneLayer layer, PointF offset) : IDrawable
{
    /// <summary>Gets the canonical drawable this instance repeats; never a clone.</summary>
    internal IDrawable Owner { get; } = owner;

    /// <summary>Gets the wrapped layer that supplies the repetition lattice.</summary>
    internal SceneLayer Layer { get; } = layer;

    /// <summary>Gets the world-space translation of this image, in pixels.</summary>
    internal PointF Offset { get; } = offset;

    /// <inheritdoc/>
    public Guid Id => Owner.Id;

    /// <inheritdoc/>
    public string? Nickname => Owner.Nickname;

    /// <inheritdoc/>
    public bool Visible => Owner.Visible;

    /// <inheritdoc/>
    public int ZOrder => Owner.ZOrder;

    /// <inheritdoc/>
    public RectangleF GetDrawLocationScreen(View view)
    {
        using var scope = Enter(view);
        return Owner.GetDrawLocationScreen(view);
    }

    /// <inheritdoc/>
    public void Draw(BackbufferBase backbuffer, RectangleF destRectScreen) => Owner.Draw(backbuffer, destRectScreen);

    [ThreadStatic] private static WrappedDrawable? _current;
    [ThreadStatic] private static View? _view;

    /// <summary>
    /// Gets the screen-space offset contributed by the wrapped instance currently in scope, or
    /// <see cref="PointF.Empty"/> when no instance of <paramref name="layer"/> is being drawn
    /// into <paramref name="view"/>.
    /// </summary>
    /// <param name="view">The view the current render pass is drawing into.</param>
    /// <param name="layer">The layer whose world-to-screen transform is being evaluated.</param>
    /// <returns>The screen-space offset to add, in pixels.</returns>
    internal static PointF ScreenOffset(View view, SceneLayer layer)
    {
        if (_current is null || !ReferenceEquals(_view, view) || !ReferenceEquals(_current.Layer, layer))
            return PointF.Empty;
        float zoom = RenderContext.Current?.ViewportZoom ?? view.Viewport.Zoom;
        return new(_current.Offset.X * zoom, _current.Offset.Y * zoom);
    }

    /// <summary>
    /// Makes this instance the active translation for the supplied view until the returned scope
    /// is disposed. Scopes nest; disposing restores the previous instance.
    /// </summary>
    /// <param name="view">The view the current render pass is drawing into.</param>
    /// <returns>A disposable scope that restores the previous instance.</returns>
    internal IDisposable Enter(View view) => new Scope(this, view);

    private sealed class Scope : IDisposable
    {
        private readonly WrappedDrawable? _prior = _current;
        private readonly View? _priorView = _view;
        internal Scope(WrappedDrawable instance, View view) { _current = instance; _view = view; }
        public void Dispose() { _current = _prior; _view = _priorView; }
    }
}
