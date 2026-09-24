using System;
using CodeBrix.Platform.GameEngine.Audio;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic;

/// <summary>
/// Adds generated music to an <see cref="Engine"/> instance with one call.
/// </summary>
/// <remarks>
/// This is the entry point for the package. At start-up a game registers an instrument library and,
/// usually, a model package's generator - one <c>Register()</c> line each - and then calls
/// <see cref="UseGeneratedMusic(Engine, GeneratedMusicOptions)"/>; the music starts playing on the
/// engine's music bus.
/// </remarks>
public static class EngineGeneratedMusicExtensions
{
    private static readonly object Gate = new();
    private static GeneratedMusicProvider? _current;
    private static Engine? _subscribedEngine;

    /// <summary>
    /// Plays generated music with the default options: the first registered model generator (or the
    /// embedded replay when none is registered), through the default instrument library, fading in
    /// now.
    /// </summary>
    /// <param name="engine">The engine to play the music in.</param>
    /// <returns>The registered provider.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="engine"/> is null.</exception>
    public static GeneratedMusicProvider UseGeneratedMusic(this Engine engine) =>
        engine.UseGeneratedMusic(new GeneratedMusicOptions());

    /// <summary>
    /// Plays generated music with the given options: creates a <see cref="GeneratedMusicProvider"/>,
    /// registers it as the engine's streaming music provider and - when
    /// <see cref="GeneratedMusicOptions.StartImmediately"/> is on, the default - plays it through the
    /// engine's music manager with <see cref="GeneratedMusicOptions.FadeIn"/>.
    /// </summary>
    /// <param name="engine">The engine to play the music in.</param>
    /// <param name="options">What to play and how.</param>
    /// <returns>The registered provider, for <see cref="GeneratedMusicProvider.FollowUp(string)"/>,
    /// <see cref="GeneratedMusicProvider.ActiveSource"/> and <see cref="GeneratedMusicProvider.Diagnostics"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="engine"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <see cref="GeneratedMusicOptions.Preset"/> names no preset of any family, or the master volume
    /// is negative.
    /// </exception>
    /// <remarks>
    /// <para>
    /// CALL IT AGAIN TO CHANGE ANYTHING - the generator, the instrument library, the preset. The
    /// provider an earlier call created is stopped, unregistered and disposed, and the new one takes
    /// its place (and starts, under <see cref="GeneratedMusicOptions.StartImmediately"/>). To change
    /// only what the SAME generator plays, without a restart, use
    /// <see cref="GeneratedMusicProvider.FollowUp(string)"/> instead.
    /// </para>
    /// <para>
    /// Nothing that goes wrong once the music starts is thrown here: a missing instrument library, a
    /// generator that is not registered or a request it refuses leave the provider
    /// <see cref="StreamingMusicState.Faulted"/>, with the reason in
    /// <see cref="GeneratedMusicProvider.Fault"/> and in the engine log, and the game runs on in silence.
    /// </para>
    /// <para>
    /// The provider this method created is disposed when the engine is disposed; the game never has
    /// to dispose it.
    /// </para>
    /// </remarks>
    public static GeneratedMusicProvider UseGeneratedMusic(this Engine engine, GeneratedMusicOptions options)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);

        var provider = new GeneratedMusicProvider(options);
        GeneratedMusicProvider? previous;

        lock (Gate)
        {
            previous = _current;
            _current = provider;

            if (!ReferenceEquals(_subscribedEngine, engine))
            {
                if (_subscribedEngine is not null) { _subscribedEngine.Disposing -= OnEngineDisposing; }
                engine.Disposing += OnEngineDisposing;
                _subscribedEngine = engine;
            }
        }

        //Registering stops the provider it replaces; only then is the old one disposed, so its model is
        //  released before the new provider asks for one
        engine.Managers.StreamingMusic.Register(provider);
        previous?.Dispose();

        Engine.Logger.LogInformation(
            "Generated music registered{Replaced}{Starting}.",
            previous is null ? string.Empty : ", replacing the previous generated music",
            options.StartImmediately ? $", starting with a {options.FadeIn.TotalSeconds:0.##} s fade-in" : " (not started)");

        if (options.StartImmediately)
        {
            string key = string.IsNullOrWhiteSpace(options.TrackKey) ? GeneratedMusicOptions.DefaultTrackKey : options.TrackKey;
            MusicManager.Instance.Play(new StreamingMusicTrack(key, provider), options.FadeIn);
        }

        return provider;
    }

    /// <summary>The provider the last call created, for tests.</summary>
    internal static GeneratedMusicProvider? Current
    {
        get { lock (Gate) { return _current; } }
    }

    /// <summary>Forgets the provider the last call created, disposing it; for tests.</summary>
    internal static void ResetForTests()
    {
        GeneratedMusicProvider? current;

        lock (Gate)
        {
            current = _current;
            _current = null;
        }

        current?.Dispose();
    }

    private static void OnEngineDisposing()
    {
        GeneratedMusicProvider? current;

        lock (Gate)
        {
            current = _current;
            _current = null;
        }

        //The engine unregisters (stops) its provider as it goes; this disposes the one created here
        current?.Dispose();
    }
}
