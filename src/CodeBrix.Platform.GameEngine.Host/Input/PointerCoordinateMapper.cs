using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Rendering;
using Microsoft.UI.Xaml;

namespace CodeBrix.Platform.GameEngine.Host.Input; //CodeBrix (not from Gondwana)

/// <summary>
/// Normalizes pointer positions for the input adapters: a pointer event reports a position in its
/// element's own pixels, while every engine input API works in logical Backbuffer ScreenPx. The
/// render surface adapter owns the mapping between the two, and it is the same transform the frame
/// is presented with — so what is drawn and what is clicked can never disagree.
/// </summary>
/// <remarks>
/// The adapter is resolved lazily, and only for a <see cref="GameSurfaceCanvas"/>: an input adapter
/// is routinely attached to an element before the canvas has built its scene pipeline, and asking
/// for the pipeline early would fix the render tier before the game has chosen it. Until an adapter
/// is available — and for any other element — positions pass through unchanged.
/// </remarks>
internal sealed class PointerCoordinateMapper
{
    private readonly GameSurfaceCanvas? _canvas;
    private RenderSurfaceAdapterBase? _adapter;

    /// <summary>
    /// Initializes a new instance of the <see cref="PointerCoordinateMapper"/> class for the element
    /// whose pointer events are being translated.
    /// </summary>
    /// <param name="element">The element the input adapter listens to.</param>
    internal PointerCoordinateMapper(UIElement? element) => _canvas = element as GameSurfaceCanvas;

    /// <summary>
    /// Gets the render surface adapter positions are mapped through, or <see langword="null"/> while
    /// there is none (no scene pipeline yet, presenter mode, or a plain element).
    /// </summary>
    internal RenderSurfaceAdapterBase? Adapter => _adapter ??= _canvas?.CurrentRenderSurfaceAdapter;

    /// <summary>
    /// Converts a pointer position in element pixels to logical Backbuffer ScreenPx.
    /// </summary>
    /// <param name="position">The position a pointer event reported, in element pixels.</param>
    /// <returns>The position in logical Backbuffer ScreenPx.</returns>
    internal Point ToScreenPx(global::Windows.Foundation.Point position)
        => ToScreenPx(Adapter, (float)position.X, (float)position.Y);

    /// <summary>
    /// Converts a pointer position in adapter pixels to logical Backbuffer ScreenPx, or floors it
    /// unchanged when there is no adapter to map it through.
    /// </summary>
    /// <remarks>
    /// A position over the letterbox margins keeps its outside coordinates rather than being clamped
    /// to the image, so a captured pointer, a drag and a leave notification all stay routable.
    /// </remarks>
    /// <param name="adapter">The render surface adapter to map through, or <see langword="null"/>.</param>
    /// <param name="x">The x position in adapter pixels.</param>
    /// <param name="y">The y position in adapter pixels.</param>
    /// <returns>The position in logical Backbuffer ScreenPx.</returns>
    internal static Point ToScreenPx(RenderSurfaceAdapterBase? adapter, float x, float y)
        => adapter is null
            ? new Point((int)MathF.Floor(x), (int)MathF.Floor(y))
            : adapter.AdapterPxToScreenPx(new PointF(x, y));
}
