using System;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Rendering; //CodeBrix (not from Gondwana)

/// <summary>
/// Converts an <see cref="SKImage"/> to a raw RGBA8888 byte buffer — the Skia-free bitmap
/// shape the engine hands to hosting applications (4 bytes per pixel in R,G,B,A memory
/// order, row-major, unpremultiplied alpha, <c>width * height * 4</c> bytes total). This is
/// the layout imaging libraries load directly (for example CodeBrix.Imaging's
/// <c>Image.LoadPixelData&lt;Rgba32&gt;(bytes, width, height)</c>).
/// </summary>
internal static class RgbaPixelExport
{
    /// <summary>
    /// Reads <paramref name="image"/> into a new RGBA8888 byte buffer, converting from
    /// whatever color layout the image uses internally. Returns <c>null</c> (with zero
    /// dimensions) when <paramref name="image"/> is <c>null</c> or the read fails.
    /// </summary>
    /// <param name="image">The image to convert, or <c>null</c>.</param>
    /// <param name="width">The image width in pixels; 0 when the result is <c>null</c>.</param>
    /// <param name="height">The image height in pixels; 0 when the result is <c>null</c>.</param>
    /// <returns>The RGBA8888 pixel bytes, or <c>null</c>.</returns>
    internal static byte[]? FromImage(SKImage? image, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (image is null)
        {
            return null;
        }

        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bytes = new byte[info.BytesSize];

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            if (!image.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0))
            {
                return null;
            }
        }
        finally
        {
            handle.Free();
        }

        width = info.Width;
        height = info.Height;
        return bytes;
    }
}
