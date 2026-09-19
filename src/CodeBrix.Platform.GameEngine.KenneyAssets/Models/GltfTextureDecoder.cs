using System;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.KenneyAssets.Models;

/// <summary>
/// Turns the encoded bytes of a glTF base-colour image into the payload the engine's model
/// contract carries: RGBA8888, row-major, top row first, not premultiplied.
/// </summary>
/// <remarks>
/// Decoding goes through SkiaSharp, which the engine already carries, so the library needs no
/// imaging dependency of its own. A format Skia cannot decode - a KTX2, WebP-in-DDS or otherwise
/// exotic texture, which the glTF reader passes through as opaque bytes - is reported as a failure
/// rather than thrown, so the caller can fall back to the material's base-colour factor instead of
/// failing the whole model.
/// </remarks>
internal static class GltfTextureDecoder
{
    /// <summary>
    /// Attempts to decode an encoded image into RGBA8888 bytes.
    /// </summary>
    /// <param name="imageBytes">The encoded image bytes, as the glTF document carried them.</param>
    /// <param name="rgba">
    /// The decoded pixels when the method returns <see langword="true"/>: four bytes per pixel in
    /// red, green, blue, alpha order, row-major, top row first, not premultiplied. An empty array
    /// otherwise.
    /// </param>
    /// <param name="width">The decoded width in pixels, or <c>0</c> when decoding failed.</param>
    /// <param name="height">The decoded height in pixels, or <c>0</c> when decoding failed.</param>
    /// <returns>
    /// <see langword="true"/> when the image decoded; otherwise <see langword="false"/>.
    /// </returns>
    internal static bool TryDecode(byte[]? imageBytes, out byte[] rgba, out int width, out int height)
    {
        rgba = [];
        width = 0;
        height = 0;

        if (imageBytes is null || imageBytes.Length == 0) { return false; }

        SKImageInfo bounds;
        try
        {
            bounds = SKBitmap.DecodeBounds(imageBytes);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (bounds.Width <= 0 || bounds.Height <= 0) { return false; }

        //Decoding straight into the wanted info is what keeps the result unpremultiplied: Skia's
        //  own default for an image with alpha is premultiplied, which would bake the alpha into
        //  the colour channels and lose the original texel colours.
        SKImageInfo info = new(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        using SKBitmap? bitmap = SKBitmap.Decode(imageBytes, info);
        if (bitmap is null || bitmap.IsEmpty) { return false; }

        width = bitmap.Width;
        height = bitmap.Height;

        int rowLength = width * 4;
        byte[] pixels = new byte[rowLength * height];
        ReadOnlySpan<byte> source = bitmap.GetPixelSpan();
        int stride = bitmap.RowBytes;

        if (source.Length < stride * (height - 1) + rowLength)
        {
            width = 0;
            height = 0;
            return false;
        }

        //Copied row by row because a bitmap's stride may exceed its row length
        for (int y = 0; y < height; y++)
        {
            source.Slice(y * stride, rowLength).CopyTo(pixels.AsSpan(y * rowLength, rowLength));
        }

        rgba = pixels;
        return true;
    }
}
