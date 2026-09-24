using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Logging;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Music that never ends: an <see cref="IStreamingMusicProvider"/> — generated music, a procedural
/// score — pulled live and played on the music bus, so <see cref="MusicManager"/> fades, crossfades,
/// ducks, pauses and stingers work on it exactly as they do on any other track.
/// </summary>
/// <remarks>
/// <para>
/// The usual way in is one call, once a provider is registered with
/// <see cref="StreamingMusicRegistry"/> (<c>Engine.Instance.Managers.StreamingMusic</c>):
/// <c>MusicManager.Instance.PlayStreaming(TimeSpan.FromSeconds(2))</c>. Constructing a track
/// yourself and handing it to <see cref="MusicManager.Play(MusicTrack, TimeSpan)"/> is the same thing
/// spelled out.
/// </para>
/// <para>
/// GAPS ARE NORMAL. A provider may take seconds to produce its first audio
/// (<see cref="StreamingMusicState.Starting"/>) and may fall behind later
/// (<see cref="StreamingMusicState.Starved"/>). The track plays silence through both, keeps pulling,
/// and NEVER stops because the music went quiet. Whatever the provider does, nothing is ever thrown
/// on the audio fill thread.
/// </para>
/// <para>
/// THE FORMAT: the provider is started at the output's real format — the pinned
/// <see cref="AudioSystem.DeviceSampleRate"/> and <see cref="AudioSystem.DeviceChannels"/> when the
/// game called <see cref="AudioSystem.Initialize"/>, otherwise the rate the shared output is running
/// at or was configured for, and only when nothing has claimed the output yet
/// <see cref="UnclaimedOutputSampleRate"/>, which the output then adopts. Calling
/// <see cref="AudioSystem.Initialize"/> at start-up is the way to choose it.
/// </para>
/// <para>
/// THE CLOCK AND THE LENGTH: <see cref="Position"/> is the time the track has been pulled for,
/// silence included, from its last start — the music's own clock. <see cref="Duration"/> is
/// <see cref="TimeSpan.Zero"/>, the <see cref="MusicTrack"/> convention for "not known", which the
/// manager and the global pause both read as endless. <see cref="IsLooping"/> is always
/// <see langword="true"/> and <see cref="Seek"/> does nothing: a stream has no length to loop or seek
/// within.
/// </para>
/// <para>
/// ENDING: <see cref="MusicTrack.Ended"/> is raised when the PROVIDER ends the stream by itself —
/// it reports <see cref="StreamingMusicState.Stopped"/> or <see cref="StreamingMusicState.Faulted"/>,
/// or someone other than this track calls its <see cref="IStreamingMusicProvider.Stop"/> (as
/// <see cref="StreamingMusicRegistry.Register"/> does to a provider it replaces). It is raised at
/// most once per start. Stopping the track through <see cref="MusicManager"/> does NOT raise it, as
/// for every music track — a playlist would otherwise advance on its own stop. After the provider
/// ends, the track keeps its voice and plays silence until the game stops or restarts it.
/// </para>
/// <para>
/// ONE STREAM PER PROVIDER. A provider is one stream, so only one track plays it at a time: starting
/// a second track over the same provider takes the stream over (the provider is stopped and started
/// afresh) and the first goes silent. Crossfading between two tracks over ONE provider therefore
/// restarts it; to change what a provider plays, use the provider's own controls.
/// </para>
/// <para>
/// OWNERSHIP: the track never disposes its provider — whoever created the provider does. Once stopped
/// the track holds no audio resources, so disposing it is tidy rather than essential.
/// </para>
/// </remarks>
public sealed class StreamingMusicTrack : MusicTrack
{
    /// <summary>
    /// The sample rate, in Hz, a provider is started at when nothing has pinned or claimed the shared
    /// output yet — the output then adopts it. 48 kHz, the rate of modern game and video audio. Call
    /// <see cref="AudioSystem.Initialize"/> to choose the rate instead.
    /// </summary>
    public const int UnclaimedOutputSampleRate = 48000;

    // The planes the provider renders into are pre-allocated at this size and a larger request is
    // rendered in chunks, so the fill thread never allocates.
    internal const int PlaneFrames = 2048;

    // Starved <-> Playing flapping is logged at most once per interval; what was skipped is counted in
    // the next line.
    internal static readonly TimeSpan StarvedLogInterval = TimeSpan.FromSeconds(5);

    private static readonly ConditionalWeakTable<IStreamingMusicProvider, ProviderLease> _leases = new();
    private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    [ThreadStatic]
    private static StreamingMusicTrack? t_rendering;

    private readonly IStreamingMusicProvider _provider;
    private readonly ProviderLease _lease;
    private readonly Func<ISampleProvider, IStreamingMusicVoice>? _voiceFactory;
    private readonly object _transportGate = new();
    private readonly object _logGate = new();
    private readonly float[] _left = new float[PlaneFrames];
    private readonly float[] _right = new float[PlaneFrames];

    private IStreamingMusicVoice? _voice;
    private bool? _suspendOnEnginePause;
    private long _framesPulled;
    private volatile int _sampleRate;
    private volatile int _channels;
    private volatile bool _started;
    private volatile bool _paused;
    private volatile bool _providerStarted;
    private volatile bool _providerEnded;
    private volatile bool _disposed;
    private volatile Exception? _renderFault;
    private int _endedRaised;
    private int _endedDeferred;

    private StreamingMusicState _lastLoggedState;
    private TimeSpan _lastFlapLog = TimeSpan.MinValue;
    private int _suppressedFlaps;

    /// <summary>Creates a track over a provider, named after the provider.</summary>
    /// <param name="provider">The provider to play. The track never disposes it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
    public StreamingMusicTrack(IStreamingMusicProvider provider)
        : this(provider?.Name ?? string.Empty, provider!, null)
    {
    }

    /// <summary>Creates a track over a provider with a name of the game's choosing.</summary>
    /// <param name="key">A name for the track, used in logs.</param>
    /// <param name="provider">The provider to play. The track never disposes it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
    public StreamingMusicTrack(string key, IStreamingMusicProvider provider)
        : this(key, provider, null)
    {
    }

    internal StreamingMusicTrack(string key, IStreamingMusicProvider provider, Func<ISampleProvider, IStreamingMusicVoice>? voiceFactory)
        : base(key)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _lease = _leases.GetValue(provider, _ => new ProviderLease());
        _voiceFactory = voiceFactory;
    }

    /// <summary>
    /// Builds the output a track plays through. The real one opens a <see cref="StreamingAudioSource"/>;
    /// tests replace it so the track can be driven without an audio device.
    /// </summary>
    internal static Func<ISampleProvider, IStreamingMusicVoice> DefaultVoiceFactory { get; set; } = CreateAudioSourceVoice;

    /// <summary>Replaces the logger, for tests that count log lines.</summary>
    internal ILogger? LoggerOverride { get; set; }

    /// <summary>Replaces the clock the starved-flapping rate limit reads, for tests.</summary>
    internal Func<TimeSpan>? ClockOverride { get; set; }

    /// <summary>The provider this track plays.</summary>
    public IStreamingMusicProvider Provider => _provider;

    /// <summary>
    /// The stream's state as this track sees it: <see cref="StreamingMusicState.Stopped"/> while the
    /// track is not playing (or another track has taken the provider over), otherwise the provider's
    /// own <see cref="IStreamingMusicProvider.State"/> — or <see cref="StreamingMusicState.Faulted"/>
    /// when the provider broke the contract by throwing.
    /// </summary>
    public StreamingMusicState State
    {
        get
        {
            if (!IsCurrentOwner)
            {
                return StreamingMusicState.Stopped;
            }

            return _renderFault is not null ? StreamingMusicState.Faulted : SafeProviderState();
        }
    }

    /// <summary>
    /// What went wrong when <see cref="State"/> is <see cref="StreamingMusicState.Faulted"/>, otherwise
    /// <see langword="null"/>.
    /// </summary>
    public Exception? Fault => _renderFault ?? (IsCurrentOwner ? _provider.Fault : null);

    /// <summary>The sample rate, in Hz, the provider was last started at; 0 before the first start.</summary>
    public int SampleRate => _sampleRate;

    /// <summary>The output channel count the provider was last started with; 0 before the first start.</summary>
    public int Channels => _channels;

    /// <summary>
    /// Overrides the global engine pause's decision for this track. Music always suspends by default
    /// (<see langword="null"/> means suspend); <see langword="false"/> keeps the stream playing through
    /// a pause, for music under a pause menu.
    /// </summary>
    public bool? SuspendOnEnginePause
    {
        get => _suspendOnEnginePause;
        set
        {
            _suspendOnEnginePause = value;
            var voice = _voice;
            if (voice is not null)
            {
                voice.SuspendOnEnginePause = value ?? true;
            }
        }
    }

    /// <summary>
    /// How long the track has been pulled for since it last started, silence included — the music's
    /// own clock. It does not advance while paused.
    /// </summary>
    public override TimeSpan Position
    {
        get
        {
            var rate = _sampleRate;
            return rate <= 0
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(Interlocked.Read(ref _framesPulled) / (double)rate);
        }
    }

    /// <summary>
    /// Always <see cref="TimeSpan.Zero"/>: a stream has no length, and zero is the
    /// <see cref="MusicTrack"/> convention for "not known", which is read as endless.
    /// </summary>
    public override TimeSpan Duration => TimeSpan.Zero;

    /// <summary>Always <see langword="true"/>: a stream never reaches an end. Setting it does nothing.</summary>
    public override bool IsLooping
    {
        get => true;
        set { }
    }

    /// <inheritdoc/>
    public override bool IsPlaying => !_disposed && _started && !_paused && (_voice?.IsPlaying ?? false);

    /// <summary>Does nothing: a stream cannot be seeked.</summary>
    /// <param name="position">Ignored.</param>
    public override void Seek(TimeSpan position)
    {
    }

    /// <inheritdoc/>
    protected override void ApplyVolume(float volume)
    {
        var voice = _voice;
        if (voice is not null)
        {
            // The voice is a StreamingAudioSource on the music bus: it applies the bus, the duck and
            // the master volume itself, so this sets only the track's own level.
            voice.Volume = volume;
        }
    }

    /// <inheritdoc/>
    internal override void StartCore(bool fromStart)
    {
        lock (_transportGate)
        {
            if (!_disposed)
            {
                if (_started && !fromStart)
                {
                    ResumeLocked();
                }
                else
                {
                    StopLocked();
                    StartLocked();
                }
            }
        }

        FlushDeferredEnded();
    }

    /// <inheritdoc/>
    internal override void PauseCore()
    {
        lock (_transportGate)
        {
            if (!_started || _paused)
            {
                return;
            }

            _paused = true;
            SafeInvoke(() => _voice?.Stop(), "pause its voice");
        }
    }

    /// <inheritdoc/>
    internal override void ResumeCore()
    {
        lock (_transportGate)
        {
            ResumeLocked();
        }
    }

    /// <inheritdoc/>
    internal override void StopCore()
    {
        lock (_transportGate)
        {
            StopLocked();
        }

        FlushDeferredEnded();
    }

    /// <summary>Stops the track and releases its voice. The provider is stopped but never disposed.</summary>
    public override void Dispose()
    {
        lock (_transportGate)
        {
            if (_disposed)
            {
                return;
            }

            StopLocked();
            _disposed = true;
        }
    }

    /// <summary>
    /// Resolves the format the output runs at: the pinned device format, else the shared output's
    /// running or configured rate, else <see cref="UnclaimedOutputSampleRate"/>.
    /// </summary>
    /// <param name="unclaimed">True when nothing has pinned or claimed the output yet.</param>
    /// <returns>The sample rate and channel count to start a provider at.</returns>
    internal static (int SampleRate, int Channels) ResolveOutputFormat(out bool unclaimed)
    {
        unclaimed = false;

        if (AudioSystem.IsInitialized)
        {
            return (AudioSystem.DeviceSampleRate, AudioSystem.DeviceChannels);
        }

        var channels = SharedAudioOutput.Channels is 1 or 2 ? SharedAudioOutput.Channels : 2;
        var rate = SharedAudioOutput.SampleRate;

        if (rate > 0)
        {
            return (rate, channels);
        }

        unclaimed = true;
        return (UnclaimedOutputSampleRate, channels);
    }

    /// <summary>
    /// Fills one interleaved output buffer from the provider — the whole of the audio fill path.
    /// Never throws; whatever the provider does not supply is silence.
    /// </summary>
    /// <param name="buffer">The interleaved buffer to fill completely.</param>
    internal void Fill(Span<float> buffer)
    {
        var channels = Math.Max(1, _channels);
        var frames = buffer.Length / channels;
        var produced = 0;

        var outer = t_rendering;
        t_rendering = this;

        try
        {
            lock (_lease)
            {
                if (ReferenceEquals(_lease.Owner, this) && _providerStarted && !_providerEnded && !_disposed)
                {
                    produced = RenderLocked(buffer, frames, channels);
                }
            }
        }
        catch (Exception ex)
        {
            produced = 0;
            HandleFault(ex, "rendering");
        }
        finally
        {
            t_rendering = outer;
        }

        buffer[(produced * channels)..].Clear();
        Interlocked.Add(ref _framesPulled, frames);

        FlushDeferredEnded();
    }

    // Callers hold _lease.
    private int RenderLocked(Span<float> buffer, int frames, int channels)
    {
        var done = 0;

        while (done < frames)
        {
            var want = Math.Min(PlaneFrames, frames - done);
            var got = _provider.Render(_left.AsSpan(0, want), _right.AsSpan(0, want));
            got = Math.Clamp(got, 0, want);

            Interleave(buffer[(done * channels)..], got, channels);
            done += got;

            if (got < want)
            {
                break; // the provider has nothing more right now: the rest is silence
            }
        }

        return done;
    }

    private void Interleave(Span<float> destination, int frames, int channels)
    {
        if (channels == 2)
        {
            for (var i = 0; i < frames; i++)
            {
                destination[2 * i] = _left[i];
                destination[(2 * i) + 1] = _right[i];
            }

            return;
        }

        if (channels == 1)
        {
            for (var i = 0; i < frames; i++)
            {
                destination[i] = 0.5f * (_left[i] + _right[i]);
            }

            return;
        }

        for (var i = 0; i < frames; i++)
        {
            var frame = destination.Slice(i * channels, channels);
            frame.Clear();
            frame[0] = _left[i];
            frame[1] = _right[i];
        }
    }

    // Callers hold _transportGate.
    private void StartLocked()
    {
        var (rate, channels) = ResolveOutputFormat(out var unclaimed);
        _sampleRate = rate;
        _channels = channels;

        Interlocked.Exchange(ref _framesPulled, 0);
        Interlocked.Exchange(ref _endedRaised, 0);
        Interlocked.Exchange(ref _endedDeferred, 0);
        _providerEnded = false;
        _providerStarted = false;
        _renderFault = null;
        _paused = false;

        lock (_logGate)
        {
            _lastLoggedState = StreamingMusicState.Stopped;
            _lastFlapLog = TimeSpan.MinValue;
            _suppressedFlaps = 0;
        }

        TakeLease();

        _provider.StateChanged += OnProviderStateChanged;
        _started = true;

        Logger.LogInformation(
            "Streaming music '{Key}': starting '{Provider}' at {SampleRate} Hz, {Channels} channel(s){Note}.",
            Key,
            SafeProviderName(),
            rate,
            channels,
            unclaimed
                ? " (nothing had pinned or claimed the audio output, so it adopts this rate; call AudioSystem.Initialize to choose one)"
                : string.Empty);

        try
        {
            _provider.Start(rate, channels);
            _providerStarted = true;
        }
        catch (Exception ex)
        {
            HandleFault(ex, "starting");
        }

        try
        {
            var factory = _voiceFactory ?? DefaultVoiceFactory;
            var voice = factory(new TrackSampleStream(this, WaveFormat.CreateIeeeFloatWaveFormat(rate, channels)));
            voice.SuspendOnEnginePause = _suspendOnEnginePause ?? true;
            voice.Volume = Volume;
            _voice = voice;
            voice.Start();
        }
        catch
        {
            StopLocked();
            throw;
        }
    }

    // Callers hold _transportGate.
    private void ResumeLocked()
    {
        if (!_started || !_paused)
        {
            return;
        }

        _paused = false;
        _voice?.Start();
    }

    // Callers hold _transportGate.
    private void StopLocked()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _paused = false;
        _provider.StateChanged -= OnProviderStateChanged;

        bool wasOwner;
        lock (_lease)
        {
            // Once this lock is released no Render is in flight for this track, and none will start.
            wasOwner = ReferenceEquals(_lease.Owner, this);
            if (wasOwner)
            {
                _lease.Owner = null;
            }

            _providerStarted = false;
        }

        if (wasOwner)
        {
            SafeInvoke(_provider.Stop, "stop its provider");
        }

        var voice = _voice;
        _voice = null;
        if (voice is not null)
        {
            SafeInvoke(voice.Dispose, "release its voice");
        }

        Logger.LogInformation("Streaming music '{Key}': stopped.", Key);
    }

    // Callers hold _transportGate. Makes this track the provider's one listener and renderer; a track
    // that had it goes silent, and the provider is stopped so this start begins a fresh timeline.
    private void TakeLease()
    {
        StreamingMusicTrack? previous;
        lock (_lease)
        {
            previous = _lease.Owner;
            _lease.Owner = this;
        }

        if (previous is not null && !ReferenceEquals(previous, this))
        {
            previous.OnSuperseded();
        }

        if (previous is not null || SafeProviderState() != StreamingMusicState.Stopped)
        {
            SafeInvoke(_provider.Stop, "stop its provider before restarting it");
        }
    }

    // Another track took the provider over. Deliberately takes no lock of this track's own: two
    // tracks starting each other at once must not deadlock. The voice keeps pulling (silence) until
    // the manager stops this track, which then leaves the provider alone.
    private void OnSuperseded()
    {
        _provider.StateChanged -= OnProviderStateChanged;
        _providerStarted = false;
        Logger.LogInformation("Streaming music '{Key}': another track took over '{Provider}'.", Key, SafeProviderName());
    }

    private void OnProviderStateChanged(object? sender, EventArgs e)
    {
        try
        {
            if (!IsCurrentOwner)
            {
                return;
            }

            var state = SafeProviderState();
            LogStateChange(state);

            if (state is StreamingMusicState.Stopped or StreamingMusicState.Faulted)
            {
                _providerEnded = true;
                RaiseEndedOnce();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Streaming music '{Key}': failed to handle a provider state change.", Key);
        }
    }

    private void HandleFault(Exception ex, string during)
    {
        if (_renderFault is not null)
        {
            return;
        }

        _renderFault = ex;
        _providerEnded = true;

        Logger.LogWarning(
            ex,
            "Streaming music '{Key}': '{Provider}' threw while {During} and is treated as Faulted; the track plays silence.",
            Key,
            SafeProviderName(),
            during);

        RaiseEndedOnce();
    }

    private void LogStateChange(StreamingMusicState state)
    {
        int suppressed;

        lock (_logGate)
        {
            if (state == _lastLoggedState)
            {
                return;
            }

            var flap = state == StreamingMusicState.Starved
                || (state == StreamingMusicState.Playing && _lastLoggedState == StreamingMusicState.Starved);

            if (flap)
            {
                var now = Now();
                if (_lastFlapLog != TimeSpan.MinValue && now - _lastFlapLog < StarvedLogInterval)
                {
                    _suppressedFlaps++;
                    return;
                }

                _lastFlapLog = now;
            }

            suppressed = _suppressedFlaps;
            _suppressedFlaps = 0;
            _lastLoggedState = state;
        }

        var detail = state switch
        {
            StreamingMusicState.Playing => " - " + SafeProviderDescription(),
            StreamingMusicState.Faulted => " - " + (_provider.Fault?.Message ?? "no reason given"),
            _ => string.Empty,
        };

        if (suppressed > 0)
        {
            detail += $" ({suppressed} starved/recovered change(s) not logged)";
        }

        Logger.LogInformation("Streaming music '{Key}': {State}{Detail}", Key, state, detail);
    }

    private void RaiseEndedOnce()
    {
        if (Interlocked.Exchange(ref _endedRaised, 1) != 0)
        {
            return;
        }

        // Never raise from inside the fill path's lock or the transport lock: a handler that starts
        // other music must not re-enter a half-finished transition. The holder raises it on the way out.
        if (ReferenceEquals(t_rendering, this) || Monitor.IsEntered(_transportGate))
        {
            Interlocked.Exchange(ref _endedDeferred, 1);
            return;
        }

        InvokeEnded();
    }

    private void FlushDeferredEnded()
    {
        if (Interlocked.Exchange(ref _endedDeferred, 0) == 1)
        {
            InvokeEnded();
        }
    }

    private void InvokeEnded()
    {
        try
        {
            RaiseEnded();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Streaming music '{Key}': an Ended handler threw.", Key);
        }
    }

    private bool IsCurrentOwner => !_disposed && _started && ReferenceEquals(_lease.Owner, this);

    private ILogger Logger => LoggerOverride ?? EngineLogger.GetLogger<StreamingMusicTrack>();

    private TimeSpan Now() => ClockOverride?.Invoke() ?? _stopwatch.Elapsed;

    private StreamingMusicState SafeProviderState()
    {
        try
        {
            return _provider.State;
        }
        catch (Exception)
        {
            return StreamingMusicState.Faulted;
        }
    }

    private string SafeProviderName()
    {
        try
        {
            return _provider.Name;
        }
        catch (Exception)
        {
            return "(unnamed provider)";
        }
    }

    private string SafeProviderDescription()
    {
        try
        {
            return _provider.Description;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private void SafeInvoke(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Streaming music '{Key}': failed to {What}.", Key, what);
        }
    }

    private static IStreamingMusicVoice CreateAudioSourceVoice(ISampleProvider stream) => new StreamingAudioSourceVoice(stream);

    // One per provider instance, shared by every track over it: whoever holds it is the provider's one
    // listener and renderer, and locking it serializes Render against start, stop and take-over.
    private sealed class ProviderLease
    {
        internal volatile StreamingMusicTrack? Owner;
    }

    // The endless interleaved stream the voice pulls; every read is filled completely.
    private sealed class TrackSampleStream : ISampleProvider
    {
        private readonly StreamingMusicTrack _track;

        internal TrackSampleStream(StreamingMusicTrack track, WaveFormat waveFormat)
        {
            _track = track;
            WaveFormat = waveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            _track.Fill(buffer);
            return buffer.Length;
        }
    }
}
