using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// The shared set-up of every test class here: the process-wide music registries back to their
/// starting state (the built-in replays, and only the synthesized General MIDI library), no streaming
/// provider or music in the engine, a pinned 48 kHz stereo output format, and engine music voices
/// that open no audio device. Everything is put back afterwards.
/// </summary>
public abstract class GeneratedMusicTestContext : IDisposable
{
    /// <summary>The output rate every test renders at.</summary>
    internal const int SampleRate = 48000;

    /// <summary>The frames one pull asks for - about 21 ms at 48 kHz, a typical device block.</summary>
    internal const int BlockFrames = 1024;

    private readonly Func<ISampleProvider, IStreamingMusicVoice> _originalVoiceFactory;

    /// <summary>Resets the registries, the engine's music and the output format.</summary>
    protected GeneratedMusicTestContext()
    {
        MusicManager.Instance.Stop();
        MusicManager.Instance.Ticker.CancelAll();
        MusicManager.Instance.Ticker.ManualTickingForTests = true;
        AudioMixer.Reset();
        EngineGeneratedMusicExtensions.ResetForTests();
        StreamingMusicRegistry.Instance.Unregister();

        MusicPackageRegistries.ResetGenerators();
        MusicPackageRegistries.ResetInstrumentLibraries();
        RegisterGeneralMidi();

        AudioSystem.Initialize(SampleRate, 2);

        _originalVoiceFactory = StreamingMusicTrack.DefaultVoiceFactory;
        StreamingMusicTrack.DefaultVoiceFactory = stream =>
        {
            var voice = new FakeMusicVoice(stream);
            lock (Voices) { Voices.Add(voice); }
            return voice;
        };
    }

    /// <summary>Every engine music voice created during the test, oldest first.</summary>
    internal List<FakeMusicVoice> Voices { get; } = [];

    /// <summary>Puts every piece of process-wide state back.</summary>
    public void Dispose()
    {
        MusicManager.Instance.Stop();
        MusicManager.Instance.Ticker.CancelAll();
        AudioMixer.Reset();
        EngineGeneratedMusicExtensions.ResetForTests();
        StreamingMusicRegistry.Instance.Unregister();
        StreamingMusicTrack.DefaultVoiceFactory = _originalVoiceFactory;
        AudioSystem.Shutdown();

        MusicPackageRegistries.ResetGenerators();
        MusicPackageRegistries.ResetInstrumentLibraries();
        RegisterGeneralMidi();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Registers the synthesized General MIDI library. Through the registry itself rather than
    /// <c>GeneralMidiInstrumentLibrary.Register()</c>, which remembers that it has run and so does
    /// nothing after a registry reset.
    /// </summary>
    internal static void RegisterGeneralMidi() => InstrumentLibraryRegistry.Register(GeneralMidiInstrumentLibrary.Instance);

    /// <summary>
    /// Pulls a provider block by block, as the engine's fill thread does, until it has produced
    /// audible sound - failing the test if that takes longer than <paramref name="timeout"/>.
    /// </summary>
    /// <returns>The number of pulls it took.</returns>
    internal static int PullUntilAudible(IStreamingMusicProvider provider, TimeSpan timeout)
    {
        var left = new float[BlockFrames];
        var right = new float[BlockFrames];
        var clock = Stopwatch.StartNew();
        var pulls = 0;

        while (clock.Elapsed < timeout)
        {
            pulls++;
            int written = provider.Render(left, right);

            if (written > 0 && (Peak(left) > 0.001f || Peak(right) > 0.001f)) { return pulls; }

            provider.State.Should().NotBe(StreamingMusicState.Faulted, provider.Fault?.ToString());
            Thread.Sleep(2);
        }

        throw new TimeoutException(
            $"'{provider.Name}' produced no audible sound in {timeout.TotalSeconds:0.#} s ({pulls} pulls); state {provider.State}.");
    }

    /// <summary>
    /// Waits, without pulling, until a provider reports a state - the start-up work runs on a worker.
    /// </summary>
    internal static void WaitForState(IStreamingMusicProvider provider, StreamingMusicState state, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();

        while (provider.State != state && clock.Elapsed < timeout) { Thread.Sleep(5); }

        provider.State.Should().Be(state, provider.Fault?.ToString());
    }

    /// <summary>The largest absolute sample in a buffer.</summary>
    internal static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (float sample in samples) { peak = Math.Max(peak, Math.Abs(sample)); }
        return peak;
    }
}
