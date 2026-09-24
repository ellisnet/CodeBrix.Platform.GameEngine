using System;
using CodeBrix.Platform.GameEngine.Logging;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Holds the game's one active <see cref="IStreamingMusicProvider"/> — the source
/// <see cref="MusicManager.PlayStreaming"/> plays from.
/// </summary>
/// <remarks>
/// <para>
/// Reach it through <c>Engine.Instance.Managers.StreamingMusic</c>. A library that supplies a
/// provider registers it here (typically from an extension method the game calls once at start-up);
/// the game then plays it with one call:
/// </para>
/// <code>
/// Engine.Instance.Managers.StreamingMusic.Register(provider);
/// MusicManager.Instance.PlayStreaming(TimeSpan.FromSeconds(2));
/// </code>
/// <para>
/// ONE PROVIDER AT A TIME. Registering a different provider STOPS the one it replaces (a track
/// playing it raises <see cref="MusicTrack.Ended"/> and goes silent) and puts the new one in its
/// place. Nothing here ever DISPOSES a provider: whoever created it owns it, and disposes it after
/// unregistering or replacing it. Engine disposal unregisters (and so stops) the active provider.
/// </para>
/// <para>The registry is safe to use from any thread; provider calls are made outside its lock.</para>
/// </remarks>
public sealed class StreamingMusicRegistry
{
    private static readonly Lazy<StreamingMusicRegistry> _instance = new(() => new StreamingMusicRegistry());

    private readonly object _gate = new();
    private IStreamingMusicProvider? _provider;

    private StreamingMusicRegistry()
    {
    }

    /// <summary>The shared registry, also reachable as <c>Engine.Instance.Managers.StreamingMusic</c>.</summary>
    public static StreamingMusicRegistry Instance => _instance.Value;

    /// <summary>The active provider, or <see langword="null"/> when none is registered.</summary>
    public IStreamingMusicProvider? Provider
    {
        get { lock (_gate) { return _provider; } }
    }

    /// <summary>Whether a provider is registered.</summary>
    public bool HasProvider => Provider is not null;

    /// <summary>
    /// Makes <paramref name="provider"/> the active provider. A different provider already registered
    /// is stopped (never disposed) and replaced; registering the active provider again does nothing.
    /// </summary>
    /// <param name="provider">The provider to make active.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
    public void Register(IStreamingMusicProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        IStreamingMusicProvider? replaced;

        lock (_gate)
        {
            if (ReferenceEquals(_provider, provider))
            {
                return;
            }

            replaced = _provider;
            _provider = provider;
        }

        if (replaced is not null)
        {
            SafeStop(replaced, "replaced");
        }

        EngineLogger.GetLogger<StreamingMusicRegistry>().LogInformation(
            "Streaming music provider '{Provider}' registered{Replaced}.",
            SafeName(provider),
            replaced is null ? string.Empty : $", replacing '{SafeName(replaced)}'");
    }

    /// <summary>
    /// Stops the active provider and removes it. The provider is NOT disposed — that stays with
    /// whoever created it.
    /// </summary>
    /// <returns>The provider that was removed, or <see langword="null"/> when none was registered.</returns>
    public IStreamingMusicProvider? Unregister()
    {
        IStreamingMusicProvider? removed;

        lock (_gate)
        {
            removed = _provider;
            _provider = null;
        }

        if (removed is not null)
        {
            SafeStop(removed, "unregistered");
        }

        return removed;
    }

    /// <summary>
    /// Creates a track over the active provider, named after it, ready for
    /// <see cref="MusicManager.Play(MusicTrack, TimeSpan)"/>.
    /// <see cref="MusicManager.PlayStreaming"/> does this and plays it in one call.
    /// </summary>
    /// <returns>A new, stopped track over the active provider.</returns>
    /// <exception cref="InvalidOperationException">No provider is registered.</exception>
    public StreamingMusicTrack CreateTrack()
    {
        var provider = Provider ?? throw new InvalidOperationException(
            "No streaming music provider is registered. Register one first with "
            + "Engine.Instance.Managers.StreamingMusic.Register(provider) - usually through the start-up "
            + "extension method of the library that supplies it.");

        return new StreamingMusicTrack(provider);
    }

    private static void SafeStop(IStreamingMusicProvider provider, string why)
    {
        try
        {
            provider.Stop();
        }
        catch (Exception ex)
        {
            EngineLogger.GetLogger<StreamingMusicRegistry>().LogError(
                ex, "Failed to stop the streaming music provider '{Provider}' when it was {Why}.", SafeName(provider), why);
        }
    }

    private static string SafeName(IStreamingMusicProvider provider)
    {
        try
        {
            return provider.Name;
        }
        catch (Exception)
        {
            return "(unnamed provider)";
        }
    }
}
