using System;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// One layer of a <see cref="MusicStemSet"/> — the drums, the strings, the combat layer — and the
/// handle a game uses to bring it in or take it out.
/// </summary>
/// <remarks>
/// A stem has a gain and nothing else: no transport of its own. It cannot be started, stopped or
/// seeked independently, because that is precisely what would break the set's sample lock. The set
/// plays; a stem is only ever louder or quieter within it.
/// </remarks>
public sealed class MusicStem
{
    private readonly MusicStemSet _owner;
    private readonly int _index;

    internal MusicStem(MusicStemSet owner, int index, string name)
    {
        _owner = owner;
        _index = index;
        Name = name;
    }

    /// <summary>The stem's name, used by <see cref="MusicStemSet"/>'s indexer.</summary>
    public string Name { get; }

    /// <summary>
    /// The layer's level within the set, 0.0 (out) to 1.0 (full). Setting it takes effect on the
    /// next audio block, ramped rather than stepped. A fade in flight will overwrite a value set
    /// directly.
    /// </summary>
    public float Gain
    {
        get => _owner.GetStemGain(_index);
        set => _owner.SetStemGain(_index, value);
    }

    /// <summary>
    /// Fades this layer to a level over a duration — the normal way to bring a stem in or out.
    /// </summary>
    /// <param name="target">The gain to end at, 0.0 to 1.0.</param>
    /// <param name="duration">How long the fade takes. Zero or less applies <paramref name="target"/> at once.</param>
    /// <remarks>
    /// The fade runs on the same clock as every other music fade, so it freezes with the global
    /// engine pause and behaves identically in both hosting modes. Starting a second fade on the
    /// same stem replaces the first from wherever it had reached.
    /// </remarks>
    public void FadeTo(float target, TimeSpan duration = default) => _owner.FadeStem(_index, target, duration);

    /// <inheritdoc/>
    public override string ToString() => $"{Name} @ {Gain:0.00}";
}
