using System;
using System.Drawing;
using System.IO;
using CodeBrix.SkiaSvg;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Materialize;

/// <summary>
/// Turns the SVG vector art in a Kenney bundle into a bitmap, which is the only form the engine's
/// two-dimensional drawing can hold.
/// </summary>
/// <remarks>
/// <para>
/// The picture is drawn by hand rather than through a picture-to-bitmap helper, because the vector
/// documents in these bundles are not all anchored at the origin: the icon sheets carry no
/// <c>viewBox</c> at all and their content starts wherever the exporter left it, so scaling without
/// translating by the picture's cull rectangle would rasterize mostly empty space.
/// </para>
/// <para>
/// A vector has no natural pixel size, so the caller says what size it wants: an explicit raster size,
/// a factor applied to the document's intrinsic size, or neither, which means the intrinsic size.
/// </para>
/// </remarks>
internal static class SvgRasterizer
{
    /// <summary>
    /// The largest width or height a rasterized vector is given. A scale factor can ask for an
    /// arbitrarily large bitmap, so a request past this size is scaled down with its aspect ratio kept.
    /// </summary>
    internal const int MaxDimension = 4096;

    /// <summary>
    /// Rasterizes an SVG document read from a stream.
    /// </summary>
    /// <param name="svgStream">The stream holding the SVG document. It is read from its current position; the caller keeps ownership and disposes it.</param>
    /// <param name="rasterSize">The pixel size to rasterize to, or <see langword="null"/> to derive the size from the document's intrinsic size.</param>
    /// <param name="scale">The factor applied to the document's intrinsic size when <paramref name="rasterSize"/> is not given, or <see langword="null"/> for the intrinsic size itself.</param>
    /// <param name="sourcePath">The path of the document inside its bundle, used in error messages.</param>
    /// <returns>The rasterized bitmap, with a transparent background. The caller owns it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="svgStream"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="rasterSize"/> has a non-positive dimension, or <paramref name="scale"/> is not greater than zero.</exception>
    /// <exception cref="InvalidDataException">Thrown when the stream does not hold a renderable SVG document.</exception>
    internal static SKBitmap Rasterize(
        Stream svgStream,
        Size? rasterSize = null,
        float? scale = null,
        string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(svgStream);

        SKSvg svg;

        try
        {
            svg = SKSvg.CreateFromStream(svgStream);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(NotRenderableMessage(sourcePath), ex);
        }

        using (svg)
        {
            SKPicture? picture = svg.Picture;
            SKRect cull = picture?.CullRect ?? SKRect.Empty;

            if (picture is null || cull.Width <= 0f || cull.Height <= 0f)
            {
                throw new InvalidDataException(NotRenderableMessage(sourcePath));
            }

            Size size = ResolveRasterSize(new SizeF(cull.Width, cull.Height), rasterSize, scale);

            SKBitmap bitmap = new(
                new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));

            using SKCanvas canvas = new(bitmap);
            canvas.Clear(SKColors.Transparent);

            //Scale first, then translate in that scaled space, so the cull rectangle's top-left
            //  corner lands on the bitmap's top-left corner
            canvas.Scale(size.Width / cull.Width, size.Height / cull.Height);
            canvas.Translate(-cull.Left, -cull.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();

            return bitmap;
        }
    }

    /// <summary>
    /// Works out the pixel size a vector is rasterized to.
    /// </summary>
    /// <param name="intrinsicSize">The document's intrinsic size, which is its cull rectangle's size.</param>
    /// <param name="rasterSize">The pixel size asked for, or <see langword="null"/> to derive one from <paramref name="intrinsicSize"/>.</param>
    /// <param name="scale">The factor applied to <paramref name="intrinsicSize"/> when <paramref name="rasterSize"/> is not given, or <see langword="null"/> for a factor of one.</param>
    /// <returns>The size to rasterize to, with each dimension at least one pixel and no dimension past <see cref="MaxDimension"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="rasterSize"/> has a non-positive dimension, or <paramref name="scale"/> is not greater than zero.</exception>
    /// <remarks>
    /// An explicit raster size wins over a scale factor and is used as asked, so a caller can
    /// deliberately rasterize to an aspect ratio the document does not have. Capping, on the other
    /// hand, always keeps the aspect ratio of what was asked for.
    /// </remarks>
    internal static Size ResolveRasterSize(SizeF intrinsicSize, Size? rasterSize, float? scale)
    {
        if (rasterSize is { } requested)
        {
            if (requested.Width <= 0 || requested.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rasterSize),
                    requested,
                    "A vector raster size must be positive in both dimensions.");
            }

            return CapToMaxDimension(requested.Width, requested.Height);
        }

        float factor = scale ?? 1.0f;

        if (factor <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), factor, "A vector scale must be greater than zero.");
        }

        int width = ToPixels(Math.Max(intrinsicSize.Width, 1f) * factor);
        int height = ToPixels(Math.Max(intrinsicSize.Height, 1f) * factor);

        return CapToMaxDimension(width, height);
    }

    private static Size CapToMaxDimension(int width, int height)
    {
        int longest = Math.Max(width, height);

        if (longest <= MaxDimension) { return new Size(width, height); }

        float factor = MaxDimension / (float)longest;

        return new Size(ToPixels(width * factor), ToPixels(height * factor));
    }

    private static int ToPixels(float value) => Math.Max(1, (int)MathF.Round(value));

    private static string NotRenderableMessage(string? sourcePath) =>
        string.IsNullOrWhiteSpace(sourcePath)
            ? "The data is not a renderable SVG document."
            : $"'{sourcePath}' is not a renderable SVG document.";
}
