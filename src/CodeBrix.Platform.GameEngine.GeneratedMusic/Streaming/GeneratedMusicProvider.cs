using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.Synth;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Logging;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic;

/// <summary>
/// Endless generated music as an engine <see cref="IStreamingMusicProvider"/>: a
/// CodeBrix.Audio.MusicGeneration <see cref="MusicSession"/> that opens no audio device of its own and
/// is pulled by the engine's music system instead.
/// </summary>
/// <remarks>
/// <para>
/// Most games never construct one: <see cref="EngineGeneratedMusicExtensions.UseGeneratedMusic(Engine, GeneratedMusicOptions)"/>
/// creates it, registers it with the engine and starts it. Construct one yourself only to register it
/// by hand with <c>Engine.Instance.Managers.StreamingMusic.Register(provider)</c>.
/// </para>
/// <para>
/// THE LIFE OF THE STREAM. <see cref="Start"/> returns at once; loading the generator and preparing
/// the first music continue in the background while <see cref="State"/> is
/// <see cref="StreamingMusicState.Starting"/>. It becomes <see cref="StreamingMusicState.Playing"/>
/// when the first music is heard, <see cref="StreamingMusicState.Starved"/> while the generator has
/// fallen behind (an ordinary state: the music waits, the game does not), and
/// <see cref="StreamingMusicState.Faulted"/> when the music cannot play - no instrument library, a
/// generator that is not registered or will not load, a request it refuses. A fault is reported
/// through <see cref="Fault"/> and never thrown into the engine. <see cref="Stop"/> ends the stream,
/// and a later <see cref="Start"/> begins a new one from the beginning.
/// </para>
/// <para>
/// OUTSIDE THE ENGINE'S CONTRACT this provider also offers <see cref="FollowUp(string)"/> (change the
/// music while it plays, at a bar line), <see cref="ActiveSource"/> and <see cref="Diagnostics"/>
/// (what is really playing and how it is doing) and <see cref="Release"/> (give a model's memory back).
/// </para>
/// <para>
/// It is also an <see cref="IGeneratedMusicSession"/>: a game's music code can hold it through that
/// interface (and start it through <see cref="IGeneratedMusicStarter"/>) so the code is testable
/// against a fake session.
/// </para>
/// <para>
/// THREADING: <see cref="Render"/> runs on the audio fill thread while the start-up work completes on
/// a worker, and every other member may be called from any thread; the provider is safe for all of
/// that. <see cref="StateChanged"/> may be raised on any of those threads.
/// </para>
/// </remarks>
public sealed class GeneratedMusicProvider : IStreamingMusicProvider, IGeneratedMusicSession
{
    /// <summary>The provider's <see cref="Name"/>: <c>"Generated music"</c>.</summary>
    public const string ProviderName = "Generated music";

    internal const string NoInstrumentLibraryFix =
        "No instrument library is registered, so the generated music cannot play. Register one at " +
        "start-up, before the music starts - GeneralMidiInstrumentLibrary.Register() (namespace " +
        "CodeBrix.Audio.ModestSynth, which arrives with this package) for the synthesized General MIDI " +
        "instruments, or FluidR3GmInstrumentLibrary.Register() for recorded ones. The game keeps running " +
        "in silence meanwhile.";

    //Anything above this is sound; a renderer's silence is exact zeros, or vanishing release tails
    private const float AudibleThreshold = 1e-5F;

    private readonly GeneratedMusicOptions _options;
    private readonly object _gate = new();
    private readonly object _renderGate = new();
    private readonly object _stateGate = new();

    //Written under _gate; volatile so the read-only members never wait for the gate
    private volatile MusicSession? _session;
    private volatile IMusicGenerator? _generator;

    //Guarded by _gate
    private CancellationTokenSource? _startCancellation;
    private MusicRequest? _pendingRequest;
    private string? _pendingGenerator;
    private int _startNumber;
    private bool _loggedReplayFallback;

    //Guarded by _renderGate
    private IMusicRenderSource? _renderSource;
    private bool _heardMusic;

    //Guarded by _stateGate for writes
    private volatile StreamingMusicState _state = StreamingMusicState.Stopped;
    private volatile Exception? _fault;

    private volatile int _sampleRate;
    private volatile bool _disposed;

    /// <summary>Creates a provider that plays generated music with the given options.</summary>
    /// <param name="options">
    /// What to play and how. The provider keeps its own copy, so changing the instance afterwards
    /// changes nothing; call <see cref="EngineGeneratedMusicExtensions.UseGeneratedMusic(Engine, GeneratedMusicOptions)"/>
    /// again with new options to change them.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <see cref="GeneratedMusicOptions.Preset"/> names no preset of any family (the message lists
    /// them), or <see cref="GeneratedMusicOptions.MasterVolume"/> is negative or not a number.
    /// </exception>
    public GeneratedMusicProvider(GeneratedMusicOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!(options.MasterVolume >= 0.0F))
        {
            throw new ArgumentException("The master volume must be zero or more.", nameof(options));
        }

        GeneratedMusicRequests.EnsurePresetExists(options.Preset);

        _options = options.Clone();
    }

    /// <summary>
    /// Raised after <see cref="State"/> changes - on the audio fill thread, the start-up worker or the
    /// thread that called <see cref="Stop"/>. Handlers must be quick.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>Always <see cref="ProviderName"/>.</summary>
    public string Name => ProviderName;

    /// <summary>
    /// One line saying what is playing: the generator, the instrument library and the voicing once
    /// the music has started, and what is being started before that.
    /// </summary>
    public string Description
    {
        get
        {
            MusicSession? session = _session;
            IMusicGenerator? generator = _generator;

            ActiveMusicSource? source = SafeActiveSource(session);
            if (source is not null) { return source.ToString(); }

            return generator is null
                ? $"{ProviderName} (not started)"
                : $"{generator.Name} ({generator.Family}), starting";
        }
    }

    /// <inheritdoc />
    public StreamingMusicState State => _state;

    /// <inheritdoc />
    public Exception? Fault => _fault;

    /// <summary>A copy of the options this provider plays with.</summary>
    public GeneratedMusicOptions Options => _options.Clone();

    /// <summary>The sample rate, in Hz, the provider was last started at; 0 before the first start.</summary>
    public int SampleRate => _sampleRate;

    /// <summary>
    /// What is really making the music - the generator, the instrument library, the voicing and what
    /// every part that has sounded is played with - or <see langword="null"/> until the music has
    /// started. <see cref="ActiveMusicSource.IsReplay"/> says when it is the embedded replay rather
    /// than a model. After a <see cref="FollowUp(string)"/> it changes when the new music takes over,
    /// not when it was asked for.
    /// </summary>
    public ActiveMusicSource? ActiveSource
    {
        get
        {
            MusicSession? session = _session;
            return SafeActiveSource(session);
        }
    }

    /// <summary>
    /// <see cref="ActiveSource"/> as one line for a log or a HUD, or an empty string until the music
    /// has started.
    /// </summary>
    public string ActiveSourceSummary => ActiveSource?.ToString() ?? string.Empty;

    /// <summary>
    /// <see cref="ActiveSource"/> as plain values a game (or its test fake) can construct, or
    /// <see langword="null"/> until the music has started.
    /// </summary>
    public GeneratedMusicSourceInfo? ActiveSourceInfo
    {
        get
        {
            ActiveMusicSource? source = ActiveSource;
            return source is null
                ? null
                : new GeneratedMusicSourceInfo(source.GeneratorName ?? string.Empty, source.GeneratorFamily ?? string.Empty,
                                               source.InstrumentLibraryName ?? string.Empty, source.IsReplay);
        }
    }

    /// <summary>
    /// <see cref="MusicDiagnostics.StarvationGapCount"/> of <see cref="Diagnostics"/>: how many times the
    /// music has waited for the generator; 0 before the first start.
    /// </summary>
    public int StarvationGapCount => Diagnostics?.StarvationGapCount ?? 0;

    /// <summary><see cref="Diagnostics"/> as one line for a log, or an empty string before the first start.</summary>
    public string DiagnosticsSummary => Diagnostics?.ToString() ?? string.Empty;

    /// <summary>
    /// How the music is doing - starvation gaps, the measured real-time factor, the delivery mode, the
    /// lead, the seams - cheap enough to read every frame; <see langword="null"/> before the first
    /// start.
    /// </summary>
    public MusicDiagnostics? Diagnostics
    {
        get
        {
            MusicSession? session = _session;

            try
            {
                return session?.Diagnostics;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// What the generator last threw, or <see langword="null"/>. A failed
    /// <see cref="FollowUp(string)"/> is reported here while the music plays on; a failure that ends
    /// the music is also the <see cref="Fault"/>.
    /// </summary>
    public Exception? GenerationError
    {
        get
        {
            MusicSession? session = _session;

            try
            {
                return session?.GenerationError;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }
    }

    /// <summary>Replaces the logger, for tests that count log lines.</summary>
    internal ILogger? LoggerOverride { get; set; }

    private ILogger Logger => LoggerOverride ?? EngineLogger.GetLogger<GeneratedMusicProvider>();

    /// <summary>
    /// Starts the music at the engine's output rate. Returns at once: the generator loads and the
    /// first music is prepared in the background, with <see cref="State"/> reporting
    /// <see cref="StreamingMusicState.Starting"/> until it is heard.
    /// </summary>
    /// <param name="sampleRate">The engine output's sample rate, in Hz; the music is rendered at it.</param>
    /// <param name="channels">The engine output's channel count, for information: rendering is always two planes.</param>
    /// <remarks>
    /// Never throws. Anything that stops the music from starting - including starting a disposed
    /// provider - is reported as <see cref="StreamingMusicState.Faulted"/>.
    /// </remarks>
    public void Start(int sampleRate, int channels)
    {
        if (_disposed)
        {
            SetState(StreamingMusicState.Faulted, new ObjectDisposedException(nameof(GeneratedMusicProvider)));
            return;
        }

        Stop();

        MusicSession? session = null;
        Exception? failure = null;
        bool fellBack = false;
        int startNumber;
        CancellationToken token;

        lock (_gate)
        {
            startNumber = ++_startNumber;
            _sampleRate = sampleRate;
            _startCancellation = new CancellationTokenSource();
            token = _startCancellation.Token;

            try
            {
                MusicGenerationOptions sessionOptions = GeneratedMusicRequests.ToSessionOptions(
                    _options, sampleRate, out IMusicGenerator generator, out fellBack);

                DisposeSessionLocked();
                _generator = generator;
                _session = session = new MusicSession(sessionOptions);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (fellBack && !_loggedReplayFallback)
            {
                _loggedReplayFallback = true;
                Logger.LogInformation(
                    "Generated music: no music generator is registered, so the embedded replay plays - a recording, " +
                    "the same piece every time{Unused}. Register a model package's generator at start-up (for example " +
                    "SkyTNTModel.Register() or MuPTModel.Register()) to hear generated music.",
                    GeneratedMusicRequests.DescribeUnusedRequestParts(_options));
            }
        }

        SetState(StreamingMusicState.Starting, null);

        if (failure is not null)
        {
            Fail(startNumber, failure);
            return;
        }

        _ = Task.Run(() => RunStartAsync(startNumber, session!, token), CancellationToken.None);
    }

    /// <summary>
    /// Stops the music. Returns promptly, is safe to call when already stopped, and a later
    /// <see cref="Start"/> begins new music from the beginning. The loaded model stays loaded; see
    /// <see cref="Release"/>.
    /// </summary>
    public void Stop()
    {
        MusicSession? session;

        lock (_gate)
        {
            _startNumber++;
            _pendingRequest = null;
            _pendingGenerator = null;

            if (_startCancellation is not null)
            {
                _startCancellation.Cancel();
                _startCancellation.Dispose();
                _startCancellation = null;
            }

            lock (_renderGate)
            {
                //Once this is released no Render is using the renderer, and none will
                _renderSource = null;
                _heardMusic = false;
            }

            session = _session;

            try
            {
                session?.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Generated music: stopping the music session failed.");
            }
        }

        SetState(StreamingMusicState.Stopped, null);
    }

    /// <summary>
    /// Renders the next frames of music into two planes. Called by the engine on the audio fill
    /// thread; it does not allocate and never throws.
    /// </summary>
    /// <param name="left">The left channel; its length is the frames wanted.</param>
    /// <param name="right">The right channel; the same length as <paramref name="left"/>.</param>
    /// <returns>
    /// The frames written: all of them once the music has started (silence while it waits for the
    /// generator), none while starting, stopped or faulted.
    /// </returns>
    public int Render(Span<float> left, Span<float> right)
    {
        StreamingMusicState next;
        Exception? fault = null;
        int written;

        try
        {
            lock (_renderGate)
            {
                IMusicRenderSource? source = _renderSource;

                if (source is null) { return 0; }

                int frames = Math.Min(left.Length, right.Length);
                source.Render(left[..frames], right[..frames]);
                written = frames;

                //What was actually rendered decides whether music is being heard: the renderer can
                //  already be sounding (a seam's tail, a primed segment) while the session still
                //  reads as starved, and a rest in the music is silent without being starved
                bool audible = IsAudible(left[..frames]) || IsAudible(right[..frames]);

                Exception? error = source.GenerationError;
                bool finished = source.IsFinished;

                if (error is not null && (finished || !source.IsPlaying))
                {
                    next = StreamingMusicState.Faulted;
                    fault = error;
                }
                else if (finished)
                {
                    next = StreamingMusicState.Stopped;
                }
                else if (audible)
                {
                    _heardMusic = true;
                    next = StreamingMusicState.Playing;
                }
                else if (!_heardMusic)
                {
                    next = StreamingMusicState.Starting;
                }
                else
                {
                    next = source.IsStarved ? StreamingMusicState.Starved : StreamingMusicState.Playing;
                }

                if (next is StreamingMusicState.Faulted or StreamingMusicState.Stopped)
                {
                    _renderSource = null;
                }
            }
        }
        catch (Exception ex)
        {
            lock (_renderGate)
            {
                _renderSource = null;
            }

            next = StreamingMusicState.Faulted;
            fault = ex;
            written = 0;
        }

        if (next != _state) { SetState(next, fault); }

        return written;
    }

    /// <summary>
    /// Changes the music while it plays: to a preset of the playing generator's family when
    /// <paramref name="presetOrText"/> names one, otherwise to what the words ask for. The new music
    /// takes over at a bar line once it has its own pre-roll; the music never stops meanwhile.
    /// </summary>
    /// <param name="presetOrText">
    /// A preset name (<c>"ClubArrangement"</c>, <c>"JigInD"</c> ...) or words. Words that are all
    /// known character words (<c>"dark driving"</c>) become character words; anything else is free
    /// text, which neither built-in model family reads.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="presetOrText"/> is blank, or names a preset written for another family than
    /// the playing generator's (the message lists the ones that fit).
    /// </exception>
    /// <exception cref="InvalidOperationException">The music is not started, or has faulted.</exception>
    /// <exception cref="MusicGenerationException">
    /// The generator refuses the request (for example free text) - the message names what it will not
    /// honour, and THE MUSIC CARRIES ON as it was.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    /// <remarks>
    /// Called while the music is still starting, the change is kept and made as soon as the music
    /// plays; a second change before then replaces the first.
    /// </remarks>
    public void FollowUp(string presetOrText)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(presetOrText))
        {
            throw new ArgumentException("Say what the music should change to: a preset name or words.", nameof(presetOrText));
        }

        lock (_gate)
        {
            IMusicGenerator generator = PlayingGeneratorLocked();

            MusicRequest request = IsAnyPresetName(presetOrText)
                ? GeneratedMusicRequests.BuildRequest(generator, presetOrText, null, null, null, out _)
                : GeneratedMusicRequests.FromWords(presetOrText);

            FollowUpLocked(request, null, generator);
        }
    }

    /// <summary>
    /// Changes the music while it plays, from a set of options: the generator (when named - otherwise
    /// the playing one carries on), and the preset, words, seed and tempo to ask it for. The new music
    /// takes over at a bar line once it has its own pre-roll.
    /// </summary>
    /// <param name="options">
    /// The generator and the request. <see cref="GeneratedMusicOptions.BeatsPerMinute"/> is ASKED OF
    /// THE GENERATOR here (the embedded replay refuses it). The instrument library, volume, seam and
    /// engine settings belong to the session and are not changed by a follow-up: call
    /// <see cref="EngineGeneratedMusicExtensions.UseGeneratedMusic(Engine, GeneratedMusicOptions)"/>
    /// again to change those.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The preset does not exist or does not fit the generator.</exception>
    /// <exception cref="InvalidOperationException">
    /// The music is not started or has faulted, or the named generator is not registered (the message
    /// lists what is).
    /// </exception>
    /// <exception cref="MusicGenerationException">
    /// The generator refuses the request; THE MUSIC CARRIES ON as it was.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public void FollowUp(GeneratedMusicOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            IMusicGenerator playing = PlayingGeneratorLocked();
            string? generatorName = string.IsNullOrWhiteSpace(options.Generator) ? null : options.Generator.Trim();
            IMusicGenerator target = generatorName is null ? playing : MusicGeneratorRegistry.Resolve(generatorName);

            MusicRequest request = GeneratedMusicRequests.BuildRequest(
                target, options.Preset, options.Text, options.Seed, options.BeatsPerMinute, out _);

            FollowUpLocked(request, generatorName is null ? null : target.Name, target);
        }
    }

    /// <summary>
    /// Gives back the memory the generator is holding - a model's weights. It stays registered, and
    /// the next start loads it again.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The music is starting or playing: stop it first, because a model must not be released while
    /// it is generating.
    /// </exception>
    /// <remarks>Nothing happens when the generator was never resolved or holds nothing.</remarks>
    public void Release()
    {
        lock (_gate)
        {
            if (_state is not (StreamingMusicState.Stopped or StreamingMusicState.Faulted))
            {
                throw new InvalidOperationException(
                    "The generated music is still playing. Stop it (or dispose the provider) before releasing the model.");
            }

            ReleaseGeneratorLocked();
        }
    }

    /// <summary>
    /// Stops the music, ends the music session and releases the generator's memory. The provider
    /// cannot be started again. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }

        Stop();
        _disposed = true;

        lock (_gate)
        {
            DisposeSessionLocked();
            ReleaseGeneratorLocked();
        }
    }

    private async Task RunStartAsync(int startNumber, MusicSession session, CancellationToken token)
    {
        try
        {
            await session.PreloadAsync(token).ConfigureAwait(false);

            string? summary = null;

            lock (_gate)
            {
                if (startNumber != _startNumber) { return; }

                try
                {
                    session.Play();
                }
                catch (InvalidOperationException ex) when (InstrumentLibraryRegistry.DefaultName is null)
                {
                    throw new InvalidOperationException(NoInstrumentLibraryFix, ex);
                }

                IAudioRenderer renderer = session.Renderer
                    ?? throw new InvalidOperationException("The music session started without a renderer to pull from.");

                lock (_renderGate)
                {
                    _renderSource = new SessionRenderSource(session, renderer);
                    _heardMusic = false;
                }

                if (_pendingRequest is not null)
                {
                    MusicRequest pending = _pendingRequest;
                    string? pendingGenerator = _pendingGenerator;
                    _pendingRequest = null;
                    _pendingGenerator = null;

                    try
                    {
                        session.FollowUp(pending, pendingGenerator);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Generated music: the change asked for while the music was starting was refused; the music plays on.");
                    }
                }

                summary = SafeActiveSource(session)?.ToString();
            }

            Logger.LogInformation("Generated music: started at {SampleRate} Hz - {Source}.", _sampleRate, summary);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            //Stopped while starting: nothing to report
        }
        catch (ObjectDisposedException) when (token.IsCancellationRequested || _disposed)
        {
            //Disposed while starting: nothing to report
        }
        catch (Exception ex)
        {
            Fail(startNumber, ex);
        }
    }

    private void Fail(int startNumber, Exception ex)
    {
        lock (_gate)
        {
            if (startNumber != _startNumber) { return; }
        }

        Logger.LogWarning("Generated music could not start and is silent: {Reason}", ex.Message);
        SetState(StreamingMusicState.Faulted, ex);
    }

    //Callers hold _gate
    private IMusicGenerator PlayingGeneratorLocked()
    {
        if (_session is null || _generator is null
            || _state is StreamingMusicState.Stopped or StreamingMusicState.Faulted)
        {
            throw new InvalidOperationException(
                _state == StreamingMusicState.Faulted
                    ? $"The generated music has faulted and cannot change: {_fault?.Message}"
                    : "The generated music is not playing. Start it first - with UseGeneratedMusic, or with " +
                      "MusicManager.Instance.PlayStreaming() once the provider is registered.");
        }

        return _generator;
    }

    //Callers hold _gate
    private void FollowUpLocked(MusicRequest request, string? generatorName, IMusicGenerator target)
    {
        bool started;
        lock (_renderGate) { started = _renderSource is not null; }

        if (!started)
        {
            //Still starting: keep the latest change and make it once the music plays
            _pendingRequest = request;
            _pendingGenerator = generatorName;
            _generator = target;
            return;
        }

        _session!.FollowUp(request, generatorName);
        _generator = target;
    }

    //Callers hold _gate
    private void DisposeSessionLocked()
    {
        MusicSession? session = _session;
        _session = null;

        if (session is null) { return; }

        try
        {
            session.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Generated music: disposing the music session failed.");
        }
    }

    //Callers hold _gate
    private void ReleaseGeneratorLocked()
    {
        IMusicGenerator? generator = _generator;
        if (generator is null) { return; }

        try
        {
            generator.Release();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Generated music: releasing the generator '{Generator}' failed.", generator.Name);
        }
    }

    private void SetState(StreamingMusicState state, Exception? fault)
    {
        lock (_stateGate)
        {
            if (_state == state && ReferenceEquals(_fault, fault)) { return; }

            _fault = state == StreamingMusicState.Faulted ? fault : null;
            _state = state;
        }

        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Generated music: a StateChanged handler threw.");
        }
    }

    private static bool IsAnyPresetName(string name)
    {
        foreach (var preset in GeneratedMusicRequests.AllPresets)
        {
            if (string.Equals(preset.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) { return true; }
        }

        return false;
    }

    /// <summary>
    /// Puts a render source in place as though the music had just started, for tests that drive the
    /// state derivation of <see cref="Render"/> with a scripted source.
    /// </summary>
    /// <param name="source">The source to render from.</param>
    internal void AttachRenderSourceForTests(IMusicRenderSource source)
    {
        lock (_renderGate)
        {
            _renderSource = source;
            _heardMusic = false;
        }

        SetState(StreamingMusicState.Starting, null);
    }

    private static bool IsAudible(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
        {
            if (sample > AudibleThreshold || sample < -AudibleThreshold) { return true; }
        }

        return false;
    }

    private static ActiveMusicSource? SafeActiveSource(MusicSession? session)
    {
        try
        {
            return session?.ActiveSource;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }
}
