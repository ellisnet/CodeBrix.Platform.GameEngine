using System;
using CodeBrix.Platform.GameEngine.Physics.Movement.Easing;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.Effects; //was previously: Gondwana.Effects;

/// <summary>Base implementation shared by directional fill and erase effects.</summary>
public abstract class WipeEffect : DisplayEffect
{
    private readonly bool _isFill;
    private float _originalReveal;
    private EffectDirection _originalDirection;
    private float _startReveal;
    private float _targetReveal;

    private protected WipeEffect(
        EffectDirection direction,
        float durationSeconds,
        EasingKind easing,
        bool isFill)
        : base(durationSeconds, easing)
    {
        if (direction == EffectDirection.None)
            throw new ArgumentOutOfRangeException(nameof(direction));

        Direction = direction;
        _isFill = isFill;
    }

    /// <summary>Gets the direction in which the wipe travels.</summary>
    public EffectDirection Direction { get; }

    internal override EffectChannel Channel => EffectChannel.Reveal;

    internal override bool SupportsTarget(object target) =>
        target is View or SceneLayer;

    private protected override void OnStarting()
    {
        _originalReveal = EffectTargetAccess.GetReveal(Target);
        _originalDirection = EffectTargetAccess.GetRevealDirection(Target);

        if (_isFill)
        {
            _startReveal = _originalReveal >= 0.9999f ? 0f : _originalReveal;
            _targetReveal = 1f;
        }
        else
        {
            _startReveal = _originalReveal;
            _targetReveal = 0f;
        }

        EffectTargetAccess.SetReveal(Target, _startReveal, Direction);
    }

    private protected override void ApplyProgress(float progress) =>
        EffectTargetAccess.SetReveal(
            Target,
            _startReveal + (_targetReveal - _startReveal) * progress,
            Direction);

    private protected override void RestoreOriginalState() =>
        EffectTargetAccess.SetReveal(Target, _originalReveal, _originalDirection);
}

/// <summary>Directionally reveals a view or scene layer.</summary>
public sealed class FillEffect : WipeEffect
{
    /// <summary>Creates a directional fill effect.</summary>
    /// <param name="direction">The direction the reveal travels in.</param>
    /// <param name="durationSeconds">How long the wipe takes; values at or below zero complete immediately.</param>
    /// <param name="easing">The easing curve applied to the wipe progress.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is <see cref="EffectDirection.None"/>.</exception>
    public FillEffect(
        EffectDirection direction,
        float durationSeconds,
        EasingKind easing = EasingKind.Linear)
        : base(direction, durationSeconds, easing, isFill: true)
    {
    }
}

/// <summary>Directionally removes a view or scene layer from presentation.</summary>
public sealed class EraseEffect : WipeEffect
{
    /// <summary>Creates a directional erase effect.</summary>
    /// <param name="direction">The direction the erase travels in.</param>
    /// <param name="durationSeconds">How long the wipe takes; values at or below zero complete immediately.</param>
    /// <param name="easing">The easing curve applied to the wipe progress.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is <see cref="EffectDirection.None"/>.</exception>
    public EraseEffect(
        EffectDirection direction,
        float durationSeconds,
        EasingKind easing = EasingKind.Linear)
        : base(direction, durationSeconds, easing, isFill: false)
    {
    }
}
