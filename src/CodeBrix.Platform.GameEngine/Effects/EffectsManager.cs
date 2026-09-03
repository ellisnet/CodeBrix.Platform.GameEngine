using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using CodeBrix.Platform.GameEngine.Timers;

namespace CodeBrix.Platform.GameEngine.Effects; //was previously: Gondwana.Effects;

/// <summary>
/// Owns and advances display effects for one render surface host.
/// </summary>
/// <remarks>
/// Effects advance on the engine's foreground (render) cadence rather than on wall-clock time,
/// so a paused engine freezes every running effect where it stands. The engine shifts this
/// manager's time baseline past the paused interval on resume, which keeps the first resumed
/// frame from advancing an effect by the whole length of the pause.
/// </remarks>
public sealed class EffectsManager : IDisposable
{
    private readonly object _sync = new();
    private readonly List<DisplayEffect> _activeEffects = [];
    private readonly RenderSurfaceHostBase _host;
    private long _lastTick = HighResTimer.GetCurrentTick();
    private bool _disposed;

    internal EffectsManager(RenderSurfaceHostBase host) =>
        _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>Gets a snapshot of the effects that are currently running.</summary>
    public ReadOnlyCollection<DisplayEffect> ActiveEffects
    {
        get
        {
            lock (_sync)
                return _activeEffects.ToList().AsReadOnly();
        }
    }

    /// <summary>Starts an effect targeting a view owned by this render surface.</summary>
    /// <typeparam name="TEffect">The concrete effect type being started.</typeparam>
    /// <param name="target">The view the effect applies to.</param>
    /// <param name="effect">The effect instance to start; an instance can be run only once.</param>
    /// <returns>The same <paramref name="effect"/> instance, now started.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="effect"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The view is not owned by this host, or the effect does not support view targets.
    /// </exception>
    /// <exception cref="InvalidOperationException">The effect instance has already been run.</exception>
    /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
    public TEffect Run<TEffect>(View target, TEffect effect)
        where TEffect : DisplayEffect => RunCore(target, effect);

    /// <summary>Starts an effect targeting a scene layer in the currently bound scene.</summary>
    /// <typeparam name="TEffect">The concrete effect type being started.</typeparam>
    /// <param name="target">The scene layer the effect applies to.</param>
    /// <param name="effect">The effect instance to start; an instance can be run only once.</param>
    /// <returns>The same <paramref name="effect"/> instance, now started.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="effect"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The layer does not belong to this host's bound scene, or the effect does not support
    /// scene-layer targets.
    /// </exception>
    /// <exception cref="InvalidOperationException">The effect instance has already been run.</exception>
    /// <exception cref="ObjectDisposedException">This manager has been disposed.</exception>
    public TEffect Run<TEffect>(SceneLayer target, TEffect effect)
        where TEffect : DisplayEffect => RunCore(target, effect);

    /// <summary>Cancels a running effect and restores its original presentation state.</summary>
    /// <param name="effect">The effect to cancel. Effects this manager does not own are ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
    public void Cancel(DisplayEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        bool removed;
        lock (_sync)
            removed = _activeEffects.Remove(effect);

        if (!removed)
            return;

        effect.CancelInternal(restoreState: true);
        Invalidate(effect.Target);
    }

    /// <summary>Cancels every running effect owned by this manager.</summary>
    public void CancelAll()
    {
        DisplayEffect[] snapshot;

        lock (_sync)
        {
            snapshot = _activeEffects.ToArray();
            _activeEffects.Clear();
        }

        foreach (var effect in snapshot)
        {
            effect.CancelInternal(restoreState: true);
            Invalidate(effect.Target);
        }
    }

    internal void Update(long tick)
    {
        float deltaSeconds = HighResTimer.GetDuration(_lastTick, tick);
        _lastTick = tick;
        Advance(deltaSeconds);
    }

    /// <summary>
    /// Shifts this manager's tick baseline past a paused interval so the first resumed
    /// <see cref="Update"/> sees only the time that has elapsed since the engine resumed.
    /// </summary>
    /// <param name="pausedTicks">The duration of the pause, in ticks.</param>
    /// <param name="resumeTick">The current tick at the moment of resume.</param>
    /// <remarks>
    /// Without this shift the first foreground cycle after a resume would advance every running
    /// effect by the whole length of the pause, bursting fades, wipes and slides to completion.
    /// </remarks>
    internal void ShiftTimeBaselineForResume(long pausedTicks, long resumeTick) =>
        _lastTick = HighResTimer.ShiftBaselineForResume(_lastTick, pausedTicks, resumeTick);

    internal void Advance(float deltaSeconds)
    {
        DisplayEffect[] snapshot;

        lock (_sync)
            snapshot = _activeEffects.ToArray();

        foreach (var effect in snapshot)
        {
            if (!OwnsTarget(effect.Target))
            {
                RemoveAndCancel(effect, restoreState: false);
                continue;
            }

            bool finished = effect.AdvanceInternal(deltaSeconds);
            Invalidate(effect.Target);

            if (finished)
            {
                lock (_sync)
                    _activeEffects.Remove(effect);
            }
        }
    }

    internal void Invalidate(object target)
    {
        if (target is not (View or SceneLayer))
            return;

        // The port's host clears its scene binding while a scene is being disposed, so the
        // scene can be null here even though the base class declares it non-nullable.
        var scene = _host.Scene;

        if (scene is not null)
            scene.FullRefreshNeeded = true;
    }

    private TEffect RunCore<TEffect>(object target, TEffect effect)
        where TEffect : DisplayEffect
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(effect);

        if (!OwnsTarget(target))
            throw new ArgumentException(
                "The effect target is not owned by this render surface host.",
                nameof(target));

        if (!effect.SupportsTarget(target))
            throw new ArgumentException(
                $"{effect.GetType().Name} does not support {target.GetType().Name} targets.",
                nameof(target));

        DisplayEffect? replaced;

        lock (_sync)
        {
            if (effect.Status != EffectStatus.Pending)
                throw new InvalidOperationException("An effect instance can only be run once.");

            replaced = _activeEffects.FirstOrDefault(
                active => ReferenceEquals(active.Target, target)
                          && active.Channel == effect.Channel);

            if (replaced is not null)
                _activeEffects.Remove(replaced);

            _activeEffects.Add(effect);
        }

        // Preserve the current presentation value when replacing an effect so the
        // new effect can continue from it without a one-frame reset.
        replaced?.CancelInternal(restoreState: false);

        try
        {
            effect.StartInternal(this, target);
            Invalidate(target);
        }
        catch
        {
            lock (_sync)
                _activeEffects.Remove(effect);

            throw;
        }

        if (effect.Status != EffectStatus.Running)
        {
            lock (_sync)
                _activeEffects.Remove(effect);
        }

        return effect;
    }

    private bool OwnsTarget(object target)
    {
        var scene = _host.Scene;

        return target switch
        {
            View view => _host.ViewManager.Views.Contains(view),
            SceneLayer layer => scene is not null
                                && ReferenceEquals(layer.Scene, scene)
                                && scene.SceneLayers.Contains(layer),
            _ => false
        };
    }

    private void RemoveAndCancel(DisplayEffect effect, bool restoreState)
    {
        lock (_sync)
            _activeEffects.Remove(effect);

        effect.CancelInternal(restoreState);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelAll();
    }
}
