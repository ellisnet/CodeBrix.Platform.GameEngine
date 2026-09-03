using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Physics.Movement.Easing;
using CodeBrix.Platform.GameEngine.Rendering.Views;

namespace CodeBrix.Platform.GameEngine.Effects; //was previously: Gondwana.Effects;

/// <summary>
/// Applies a decaying, presentation-only camera shake to a view.
/// </summary>
public sealed class EarthquakeEffect : DisplayEffect
{
    private readonly Random _random;
    private PointF _originalFactor;
    private PointF _originalPixels;

    /// <summary>Creates a camera-shake effect.</summary>
    /// <param name="durationSeconds">How long the shake lasts; values at or below zero complete immediately.</param>
    /// <param name="intensityPx">The maximum shake displacement, in screen pixels.</param>
    /// <param name="decay">When <see langword="true"/>, the shake amplitude falls to zero over the duration.</param>
    /// <param name="randomSeed">An optional seed, for a repeatable shake pattern in tests and replays.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="intensityPx"/> is negative.</exception>
    public EarthquakeEffect(
        float durationSeconds,
        float intensityPx = 8f,
        bool decay = true,
        int? randomSeed = null)
        : base(durationSeconds, EasingKind.Linear)
    {
        if (intensityPx < 0f)
            throw new ArgumentOutOfRangeException(nameof(intensityPx));

        IntensityPx = intensityPx;
        Decay = decay;
        _random = new Random(randomSeed ?? Random.Shared.Next());
    }

    /// <summary>Gets the maximum shake displacement in screen pixels.</summary>
    public float IntensityPx { get; }

    /// <summary>Gets whether shake intensity falls to zero over the effect duration.</summary>
    public bool Decay { get; }

    internal override EffectChannel Channel => EffectChannel.Transform;

    internal override bool SupportsTarget(object target) => target is View;

    private protected override void OnStarting()
    {
        _originalFactor = EffectTargetAccess.GetOffsetFactor(Target);
        _originalPixels = EffectTargetAccess.GetOffsetPixels(Target);
    }

    private protected override void ApplyProgress(float progress)
    {
        if (progress >= 1f || IntensityPx <= 0f)
        {
            EffectTargetAccess.SetTransform(
                Target,
                _originalFactor,
                _originalPixels);

            return;
        }

        float amplitude = Decay
            ? IntensityPx * (1f - progress)
            : IntensityPx;

        float x = ((float)_random.NextDouble() * 2f - 1f) * amplitude;
        float y = ((float)_random.NextDouble() * 2f - 1f) * amplitude;

        EffectTargetAccess.SetTransform(
            Target,
            _originalFactor,
            new PointF(
                _originalPixels.X + x,
                _originalPixels.Y + y));
    }

    private protected override void RestoreOriginalState() =>
        EffectTargetAccess.SetTransform(Target, _originalFactor, _originalPixels);
}
