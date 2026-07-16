using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.Drawing;

/// <summary>
/// Specifies the sampling quality used when an image is scaled or filtered during rendering.
/// </summary>
/// <remarks>
/// This is the engine-level replacement for the SkiaSharp <c>SKFilterQuality</c> enum, which was
/// removed in SkiaSharp 4. Each value maps to an equivalent <see cref="SKSamplingOptions"/> value
/// via <see cref="ImageFilterQualityExtensions.ToSamplingOptions(ImageFilterQuality)"/>.
/// </remarks>
public enum ImageFilterQuality
{
    /// <summary>Nearest-neighbor sampling; ideal for pixel art and pixel-perfect rendering.</summary>
    None,

    /// <summary>Bilinear filtering; faster, but may appear blurry when scaled up.</summary>
    Low,

    /// <summary>Bilinear filtering with mipmapping; a good balance of quality and performance.</summary>
    Medium,

    /// <summary>Bicubic (Mitchell) resampling; highest quality and slowest performance.</summary>
    High,
}

/// <summary>
/// Provides mapping from <see cref="ImageFilterQuality"/> to SkiaSharp sampling options.
/// </summary>
internal static class ImageFilterQualityExtensions
{
    /// <summary>
    /// Maps an <see cref="ImageFilterQuality"/> value to the equivalent SkiaSharp
    /// <see cref="SKSamplingOptions"/> used by draw and shader calls in SkiaSharp 4.
    /// </summary>
    /// <param name="quality">The engine filter-quality value.</param>
    /// <returns>The equivalent <see cref="SKSamplingOptions"/>.</returns>
    public static SKSamplingOptions ToSamplingOptions(this ImageFilterQuality quality) => quality switch
    {
        ImageFilterQuality.None => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
        ImageFilterQuality.Low => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
        ImageFilterQuality.Medium => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
        ImageFilterQuality.High => new SKSamplingOptions(SKCubicResampler.Mitchell),
        _ => SKSamplingOptions.Default,
    };
}
