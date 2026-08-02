using CodeBrix.Platform.GameEngine.Assets;
using Microsoft.Extensions.Logging;
using CodeBrix.Audio.Wave; //was previously: using NAudio.Wave;
using CodeBrix.Audio.Wave.SampleProviders; //was previously: using NAudio.Wave.SampleProviders;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Json.Extensions.References;
using static CodeBrix.Platform.GameEngine.Audio.PlatformAudioFactory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Audio; //was previously: Gondwana.Audio;
/// <summary>
/// Represents a audio resource that can be played, paused, resumed, and disposed.
/// </summary>
[JsonReferenceable]
public class AudioResource : IDisposable, IEnginePausableAudio, IMixerVoice
{
    private readonly IWavePlayer outputDevice;
    private readonly WaveStream waveStream;
    private readonly WaveFormat? rawPcmFormat;              // set only for LoadFromPcm resources
    private PanningSampleProvider? monoPanProvider;         // for mono sources only
    private StereoPanSampleProvider? stereoPanProvider;     // for stereo sources only
    private VolumeSampleProvider? volumeProvider;           // final stage
    
    private bool _stopRequested;
    private bool disposed;

    #region events

    /// <summary>
    /// Event that is raised when playback completes.
    /// Will not be raised if the audio is looping.
    /// </summary>
    public event EventHandler PlaybackCompleted;

    /// <summary>
    /// Asynchronous callback that is invoked when playback completes.
    /// Will not be invoked if the audio is looping.
    /// </summary>
    public Func<Task>? PlaybackCompletedAsync;

    /// <summary>
    /// Event that is raised when the audio resource is disposed.
    /// </summary>
    public event EventHandler Disposed;

    #endregion events

    #region constructor

    [JsonConstructor]
    internal AudioResource()
    {
        // A deserialized AudioResource is a SPEC, not a voice: it carries the source, volume, pan
        // and looping to rehydrate from (see ReloadIntoManager) and owns no device, stream or
        // unmanaged resource at all. So it must not be finalized - the finalizer exists for the
        // real thing, and running it here would call Dispose(false) against null fields.
        GC.SuppressFinalize(this);
    }

    internal AudioResource(
        string key,
        WaveStream audioStream,
        float volume = 1.0f,
        float pan = 0.0f,
        string? filePathOrExt = null,
        byte[]? rawBytes = null,
        string? tempFilePath = null,
        AssetsFileIdentifier? assetIdentifier = null,
        WaveFormat? rawPcmFormat = null,
        CachedSound? cachedData = null)
    {
        CachedData = cachedData;
        Key = key;
        waveStream = audioStream;
        this.rawPcmFormat = rawPcmFormat;
        outputDevice = new WaveOutEvent();
        outputDevice.Init(BuildAudioGraph(waveStream, volume, pan));
        outputDevice.PlaybackStopped += OnPlaybackStopped;

        AudioPauseRegistry.Register(this);
        AudioMixer.Register(this);

        // Persisted rehydration info
        AssetIdentifier = assetIdentifier;
        SourceFilePath = (assetIdentifier is null && !string.IsNullOrWhiteSpace(filePathOrExt) && File.Exists(filePathOrExt))
            ? filePathOrExt
            : null;

        var ext = Path.GetExtension(filePathOrExt ?? string.Empty);
        SourceExtension = string.IsNullOrEmpty(ext) ? null : NormalizeExt(ext);

        // Runtime-only
        OriginalBytes = rawBytes;
        TempFilePath = tempFilePath;
    }

    private ISampleProvider BuildAudioGraph(WaveStream source, float volume, float pan)
    {
        _pan = Math.Clamp(pan, -1f, 1f);
        _volume = Math.Clamp(volume, 0f, 1f); // keep the Volume property in sync with the graph's gain stage
        ISampleProvider baseProvider = source.ToSampleProvider();

        if (AudioSystem.IsInitialized && baseProvider.WaveFormat.SampleRate != AudioSystem.DeviceSampleRate)
        {
            // The app pinned the device rate (AudioSystem.Initialize) and this source's rate
            // differs — CodeBrix.Audio has no resampler, so without this stage the voice
            // initialization below would throw. Inert for apps that never pin.
            baseProvider = new VariableRateSampleProvider(baseProvider, AudioSystem.DeviceSampleRate);
        }

        int ch = baseProvider.WaveFormat.Channels;
        if (ch < 1)
        {
            Engine.Logger.LogWarning(
                "AudioResource {Key} has invalid channel count: {ChannelCount}", Key, ch);

            // just pass through, no pan stage
            volumeProvider = new VolumeSampleProvider(baseProvider)
            {
                Volume = AudioMixer.EffectiveVolume(volume, _bus)
            };

            return volumeProvider;
        }

        switch (ch)
        {
            case 1:
                // MONO -> use PanningSampleProvider (expects mono, outputs stereo)
                monoPanProvider = new PanningSampleProvider(baseProvider)
                {
                    Pan = Math.Clamp(pan, -1f, 1f)
                };
                stereoPanProvider = null;
                baseProvider = monoPanProvider; // now stereo
                break;

            case 2:
                // STEREO -> use StereoSampleProvider for balance/pan
                stereoPanProvider = new StereoPanSampleProvider(baseProvider);
                ApplyStereoPan(stereoPanProvider, pan); // set L/R gains from pan
                monoPanProvider = null;
                baseProvider = stereoPanProvider;       // stays stereo
                break;

            default:
                // >2 CH -> pick first two channels, then treat as stereo
                var mux = new MultiplexingSampleProvider(new[] { baseProvider }, 2);
                mux.ConnectInputToOutput(0, 0); // channel 0 -> output L
                mux.ConnectInputToOutput(1, 1); // channel 1 -> output R
                baseProvider = mux;

                stereoPanProvider = new StereoPanSampleProvider(baseProvider);
                ApplyStereoPan(stereoPanProvider, pan);
                monoPanProvider = null;
                baseProvider = stereoPanProvider;
                break;
        }

        // final stage: this voice's volume, scaled by its mixer bus and the master volume
        volumeProvider = new VolumeSampleProvider(baseProvider)
        {
            Volume = AudioMixer.EffectiveVolume(volume, _bus)
        };

        return volumeProvider;
    }

    #endregion constructor

    #region public properties

    /// <summary>
    /// Gets the unique key associated with this audio resource.
    /// </summary>
    public string Key { get; private set; }

    /// <summary>
    /// Gets the original byte array of the audio data, if available.
    /// </summary>
    [JsonIgnore]
    public byte[]? OriginalBytes { get; private set; }

    /// <summary>
    /// The decoded-once PCM cache when this resource qualified as a short sound effect at
    /// load time (see <see cref="AudioResourceManager.PreloadShortSoundEffectMaxSeconds"/>);
    /// clones and <see cref="SfxVoicePool"/> voices share it instead of re-decoding.
    /// </summary>
    internal CachedSound? CachedData { get; }

    /// <summary>
    /// Gets a value indicating whether this resource's samples were preloaded (decoded once
    /// to PCM in memory) at load time, making it eligible for <see cref="SfxVoicePool"/>
    /// playback and decode-free cloning.
    /// </summary>
    [JsonIgnore]
    public bool IsPreloaded => CachedData is not null;

    /// <summary>
    /// Original file path when the sound was loaded from disk (loose file).
    /// Null when loaded from an AssetsFile.
    /// </summary>
    [JsonInclude]
    public string? SourceFilePath { get; private set; }

    /// <summary>
    /// Asset identifier when the sound was loaded from an AssetsFile.
    /// Null when loaded from a loose file.
    /// </summary>
    [JsonInclude]
    public AssetsFileIdentifier? AssetIdentifier { get; private set; }

    /// <summary>
    /// Normalized file extension (".wav", ".mp3", etc) used to select the reader.
    /// </summary>
    [JsonInclude]
    public string? SourceExtension { get; private set; }

    /// <summary>
    /// Gets or sets the temporary file path used for WaveReaders that require a file on disk.
    /// </summary>
    [JsonIgnore]
    public string? TempFilePath { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the playback is currently paused.
    /// </summary>
    [JsonIgnore]
    public bool IsPaused => outputDevice.PlaybackState == PlaybackState.Paused;

    /// <summary>
    /// Gets a value indicating whether audio playback is currently active.
    /// </summary>
    [JsonIgnore]
    public bool IsPlaying => outputDevice.PlaybackState == PlaybackState.Playing;

    /// <summary>
    /// Gets the current playback state of the output device.
    /// </summary>
    [JsonIgnore]
    public PlaybackState State => outputDevice.PlaybackState;

    /// <summary>
    /// Gets or sets the current playback position within the audio stream.
    /// </summary>
    [JsonIgnore]
    public TimeSpan CurrentTime
    {
        get => waveStream.CurrentTime;
        set => Seek(value);
    }

    /// <summary>
    /// Gets the total duration of the audio represented by the wave stream.
    /// </summary>
    [JsonIgnore]
    public TimeSpan Duration => waveStream.TotalTime;

    /// <summary>
    /// Gets or sets a value indicating whether the playback is set to loop.
    /// </summary>
    public bool IsLooping { get; set; }

    [JsonInclude]
    private float _volume = 1.0f;

    /// <summary>
    /// Gets or sets the volume of the audio output.
    /// 0.0 is silent, 1.0 is full volume.
    /// </summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            ApplyMixerVolume();
        }
    }

    [JsonInclude]
    private AudioBus _bus = AudioBus.Sfx;

    /// <summary>
    /// The mixer bus this resource's <see cref="Volume"/> is scaled by. Defaults to
    /// <see cref="AudioBus.Sfx"/>; set <see cref="AudioBus.Music"/> for a track the player's music
    /// slider should control, or <see cref="AudioBus.None"/> to answer to the master volume alone.
    /// </summary>
    public AudioBus Bus
    {
        get => _bus;
        set
        {
            _bus = value;
            ApplyMixerVolume();
        }
    }

    /// <summary>
    /// Applies this resource's own volume scaled by its bus and the master volume. Called by
    /// <see cref="AudioMixer"/> whenever a bus volume changes, and whenever <see cref="Volume"/> or
    /// <see cref="Bus"/> is set.
    /// </summary>
    void IMixerVoice.ApplyMixerVolume() => ApplyMixerVolume();

    private void ApplyMixerVolume()
    {
        if (volumeProvider != null)
            volumeProvider.Volume = AudioMixer.EffectiveVolume(_volume, _bus);
    }

    [JsonInclude]
    private float _pan;

    /// <summary>
    /// Gets or sets the stereo pan position of the audio output.
    /// -1 is full left, 0 is center, and 1 is full right.
    /// </summary>
    public float Pan
    {
        get => _pan;
        set
        {
            _pan = Math.Clamp(value, -1f, 1f);

            if (monoPanProvider != null)
                monoPanProvider.Pan = _pan;
            else if (stereoPanProvider != null)
                ApplyStereoPan(stereoPanProvider, _pan);
        }
    }

    #endregion public properties

    #region public methods

    /// <summary>
    /// Starts playback of the audio stream.
    /// </summary>
    /// <remarks>If the audio is already playing, calling this method has no effect.  Ensure the audio stream
    /// is properly initialized before invoking this method.</remarks>
    /// <param name="fromStart">A value indicating whether playback should start from the beginning of the audio stream.  <see langword="true"/>
    /// to start from the beginning; otherwise, playback resumes from the current position.</param>
    public void Play(bool fromStart = true)
    {
        _stopRequested = false;

        if (fromStart)
        {
            if (IsPlaying)
                outputDevice.Stop();

            waveStream.Position = 0;
        }

        if (!IsPlaying)
            outputDevice.Play();
    }

    /// <summary>
    /// Pauses playback if it is currently active.
    /// </summary>
    /// <remarks>This method pauses the playback only if it is currently in progress.  If playback is already
    /// paused or not started, calling this method has no effect.</remarks>
    public void Pause()
    {
        if (IsPlaying)
            outputDevice.Pause();
    }

    /// <summary>
    /// Resumes playback if the output device is currently paused.
    /// </summary>
    /// <remarks>This method has no effect if the output device is not paused. Ensure that the output device
    /// is properly initialized and in a paused state before calling this method.</remarks>
    public void Resume()
    {
        if (IsPaused)
            outputDevice.Play();
    }

    /// <summary>
    /// Seeks to the specified position within the audio stream.
    /// </summary>
    /// <remarks>If the audio is currently playing, it will be paused during the seek operation and resumed
    /// afterward.</remarks>
    /// <param name="position">The position to seek to, specified as a <see cref="TimeSpan"/>.  If the value is less than <see
    /// cref="TimeSpan.Zero"/>, the position is set to the start of the stream.  If the value exceeds the total duration
    /// of the stream, the position is set to the end of the stream.</param>
    public void Seek(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        if (position > waveStream.TotalTime)
            position = waveStream.TotalTime;

        var wasPlaying = IsPlaying;

        Pause();
        waveStream.CurrentTime = position;

        if (wasPlaying)
            Resume();
    }

    /// <summary>
    /// Stops the output device, halting any ongoing audio playback.
    /// </summary>
    public void Stop()
    {
        _stopRequested = true;
        outputDevice.Stop();
    }

    /// <summary>
    /// Overrides the global engine pause's suspend decision for this resource: <c>true</c>
    /// always suspends, <c>false</c> never suspends, <c>null</c> (the default) applies the
    /// automatic exemption — a playing, non-looping clip no longer than
    /// <see cref="Configuration.EngineConfiguration.PauseShortSoundEffectSeconds"/> is treated
    /// as a fire-and-forget effect and left to ring out. A looping resource always suspends.
    /// </summary>
    public bool? SuspendOnEnginePause { get; set; }

    bool IEnginePausableAudio.IsPlayingForEnginePause
        => !disposed && outputDevice is not null && IsPlaying;

    TimeSpan? IEnginePausableAudio.KnownDurationForEnginePause
        => IsLooping || waveStream is null ? null : Duration;

    void IEnginePausableAudio.EnginePause()
    {
        if (!disposed)
        {
            Pause();
        }
    }

    void IEnginePausableAudio.EngineResume()
    {
        if (!disposed)
        {
            Resume();
        }
    }

    /// <summary>
    /// Creates a NEW, independent reader over this resource's in-memory source data, so
    /// another voice (e.g. a <see cref="SoundChannel"/>) can play the clip without touching
    /// this resource's own playback state.
    /// </summary>
    internal WaveStream CreateIndependentReader()
    {
        if (CachedData is { } cached)
        {
            return new CachedSoundWaveStream(cached); // shares the decoded PCM; no re-decode
        }

        if (OriginalBytes is null)
        {
            throw new InvalidOperationException(
                $"AudioResource '{Key}' has no in-memory source data, so it cannot be used as a {nameof(SoundChannel)} clip.");
        }

        if (rawPcmFormat is not null)
        {
            return new RawSourceWaveStream(new MemoryStream(OriginalBytes, writable: false), rawPcmFormat);
        }

        if (string.IsNullOrEmpty(SourceExtension))
        {
            throw new InvalidOperationException(
                $"AudioResource '{Key}' has no source extension, so a reader cannot be selected for it.");
        }

        var (readerFactory, requiresFile) = GetReaderFactory(SourceExtension);
        if (requiresFile)
        {
            throw new NotSupportedException(
                $"The audio format '{SourceExtension}' requires a file-based reader and cannot be used as a {nameof(SoundChannel)} clip; load the clip as raw PCM ({nameof(AudioResourceManager)}.{nameof(AudioResourceManager.LoadFromPcm)}) or .wav instead.");
        }

        return readerFactory(new MemoryStream(OriginalBytes, writable: false));
    }

    /// <summary>
    /// Ensures this audio resource is loaded into <see cref="AudioResourceManager"/> from its persisted source.
    /// If the resource is already loaded, this method will not reload it (idempotent); it will only re-apply
    /// runtime settings like Volume/Pan/IsLooping.
    /// </summary>
    /// <param name="forceReload">
    /// If true, unloads and reloads the resource even if it is already present in the manager.
    /// </param>
    internal void ReloadIntoManager(bool forceReload = false)
    {
        if (string.IsNullOrWhiteSpace(Key))
            throw new InvalidOperationException("AudioResource has no Key and cannot be reloaded.");

        var mgr = AudioResourceManager.Instance;

        // If already loaded, just apply settings and bail (idempotent).
        if (!forceReload && mgr.TryGet(Key, out var existing) && existing is not null)
        {
            existing.Volume = Volume;
            existing.Pan = Pan;
            existing.IsLooping = IsLooping;
            return;
        }

        if (forceReload && mgr.Contains(Key))
            mgr.Unload(Key); // safe: manager owns the live instance :contentReference[oaicite:2]{index=2}

        // Load from persisted source
        if (AssetIdentifier is not null && AssetIdentifier.IsValid)
        {
            using var s = AssetIdentifier.Data;
            if (s is null)
                throw new InvalidOperationException($"Missing asset data for {Key}.");

            mgr.LoadFromStream(Key, s, SourceExtension ?? ".wav", Volume, Pan);
        }
        else if (!string.IsNullOrWhiteSpace(SourceFilePath))
        {
            mgr.LoadFromFile(Key, SourceFilePath, Volume, Pan);
        }
        else
        {
            throw new InvalidOperationException($"AudioResource '{Key}' has no persisted source.");
        }

        // Apply looping after load (LoadFromStream/File sets volume/pan during graph creation)
        if (mgr.TryGet(Key, out var loaded) && loaded is not null)
            loaded.IsLooping = IsLooping;
    }

    #endregion public methods

    #region private methods

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Engine.Logger.LogError(e.Exception, "PlaybackStopped due to error for audio: {Key}\r\n{ErrorDescription}", Key, e.ToString());
        }
        else
        {
            HandlePlaybackStopped();
        }
    }

    private void HandlePlaybackStopped()
    {
        try
        {
            if (_stopRequested)
            {
                _stopRequested = false;
                return;
            }

            bool reachedEnd = waveStream.Position >= waveStream.Length;

            if (IsLooping && reachedEnd)
            {
                Play(true);
            }
            else
            {
                if (PlaybackCompletedAsync is not null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await PlaybackCompletedAsync();
                        }
                        catch (Exception ex)
                        {
                            Engine.Logger.LogError(ex, "PlaybackCompletedAsync threw an exception for audio resource: {Key}", Key);
                        }
                    });
                }

                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Engine.Logger.LogError(ex, "Error during playback completion handling for audio resource: {Key}", Key);
        }
    }

    internal static void ApplyStereoPan(StereoPanSampleProvider s, float pan)
    {
        pan = Math.Clamp(pan, -1f, 1f);
        // equal-power: map [-1..1] to [0..pi/2]
        float angle = (pan + 1f) * 0.5f * (float)(Math.PI / 2);
        s.LeftVolume = MathF.Cos(angle);
        s.RightVolume = MathF.Sin(angle);
    }

    #endregion private methods

    #region IDisposable members

    /// <summary>
    /// Releases all resources used by the <see cref="AudioResource"/> instance.
    /// </summary>
    /// <remarks>This method stops playback, disposes of the output device and wave stream, deletes any
    /// temporary files, and raises the <see cref="Disposed"/> event. After calling this method, the instance
    /// should not be used further.</remarks>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~AudioResource() => Dispose(false);

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="AudioResource"/> and optionally releases the managed resources.
    /// </summary>
    /// <remarks>This method implements the dispose pattern. When <paramref name="disposing"/> is <see langword="true"/>,
    /// it releases both managed and unmanaged resources. When <see langword="false"/>, it releases only unmanaged resources.</remarks>
    /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
            return;

        // Every field below can legitimately be null: the [JsonConstructor] overload builds a
        // rehydration spec that owns no voice (see that constructor). Disposing one of those - or
        // finalizing it, before SuppressFinalize was added there - otherwise threw a
        // NullReferenceException, and from the finalizer thread that takes the whole process down
        // rather than failing anything visible.
        if (disposing)
        {
            if (outputDevice is not null)
            {
                try
                {
                    outputDevice.PlaybackStopped -= OnPlaybackStopped;
                }
                catch
                {
                    /* noop */
                }

                Stop();
            }
        }

        outputDevice?.Dispose();
        waveStream?.Dispose();

        if (TempFilePath is not null)
        {
            try
            {
                File.Delete(TempFilePath);
            }
            catch (Exception ex)
            {
                Engine.Logger.LogError(ex, "Failed to delete temporary file {TempFilePath} for audio resource {Key}", TempFilePath, Key);
            }
        }

        disposed = true;
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    #endregion IDisposable members
}