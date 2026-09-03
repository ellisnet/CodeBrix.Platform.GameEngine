using System;
using CodeBrix.Platform.GameEngine.Physics.Movement.Easing;
using CodeBrix.Platform.GameEngine.Rendering.Views;

namespace CodeBrix.Platform.GameEngine.Effects; //was previously: Gondwana.Effects;

/// <summary>Base implementation shared by zoom-in and zoom-out effects.</summary>
/// <remarks>
/// The zoom itself is animated by the view's viewport. This effect owns only the lifecycle,
/// channel replacement, and completion notification around that animation.
/// </remarks>
public abstract class ZoomEffect : DisplayEffect
{
    private float _originalZoom;
    private float _clampedTargetZoom;

    private protected ZoomEffect(float targetZoom, float durationSeconds)
        : base(durationSeconds, EasingKind.Linear)
    {
        if (targetZoom <= 0f)
            throw new ArgumentOutOfRangeException(nameof(targetZoom));

        TargetZoom = targetZoom;
    }

    /// <summary>Gets the requested final zoom factor.</summary>
    public float TargetZoom { get; }

    internal override EffectChannel Channel => EffectChannel.Zoom;

    internal override bool SupportsTarget(object target) => target is View;

    private protected override void OnStarting()
    {
        var view = GetTarget<View>();
        _originalZoom = view.Viewport.Zoom;
        _clampedTargetZoom = Math.Clamp(TargetZoom, view.MinZoom, view.MaxZoom);
        view.Viewport.ZoomToOverDuration(_clampedTargetZoom, DurationSeconds);
    }

    // View.Update() advances the existing viewport zoom animator. The effect
    // manager owns only lifecycle, replacement, and completion notification.
    private protected override void ApplyProgress(float progress)
    {
    }

    private protected override void OnCompleted() =>
        GetTarget<View>().Viewport.SnapZoom(_clampedTargetZoom);

    private protected override void RestoreOriginalState() =>
        GetTarget<View>().Viewport.SnapZoom(_originalZoom);
}

/// <summary>Animates a view to a larger or otherwise explicitly supplied zoom factor.</summary>
public sealed class ZoomInEffect : ZoomEffect
{
    /// <summary>Creates a zoom-in effect.</summary>
    /// <param name="targetZoom">The zoom factor to animate to; clamped to the view's zoom limits.</param>
    /// <param name="durationSeconds">How long the zoom takes; values at or below zero complete immediately.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="targetZoom"/> is not greater than zero.</exception>
    public ZoomInEffect(float targetZoom, float durationSeconds)
        : base(targetZoom, durationSeconds)
    {
    }
}

/// <summary>Animates a view to a smaller or otherwise explicitly supplied zoom factor.</summary>
public sealed class ZoomOutEffect : ZoomEffect
{
    /// <summary>Creates a zoom-out effect.</summary>
    /// <param name="targetZoom">The zoom factor to animate to; clamped to the view's zoom limits.</param>
    /// <param name="durationSeconds">How long the zoom takes; values at or below zero complete immediately.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="targetZoom"/> is not greater than zero.</exception>
    public ZoomOutEffect(float targetZoom, float durationSeconds)
        : base(targetZoom, durationSeconds)
    {
    }
}
