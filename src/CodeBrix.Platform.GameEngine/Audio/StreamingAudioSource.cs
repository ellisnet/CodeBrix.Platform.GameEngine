using System;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Wave.SampleProviders;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>
/// Fills <paramref name="buffer"/> completely with interleaved 32-bit float samples at the
/// pinned device rate and channel count. Called on the audio callback thread.
/// </summary>
/// <param name="buffer">The buffer to fill; every sample must be written.</param>
public delegate void FillAudioBuffer(Span<float> buffer);

/// <summary>
/// Hosts one endless pull-model PCM stream (a soundfont synthesizer, an emulated sound chip,
/// any procedural source) on a dedicated voice of the shared audio output.
/// </summary>
/// <remarks>
/// <para>
/// The supplier — the <see cref="FillAudioBuffer"/> callback or the
/// <see cref="ISampleProvider"/> — is pulled on the AUDIO CALLBACK THREAD whenever the device
/// needs more samples. It must be fast, allocation-free, and must never block; produce
/// silence (zeros) when there is nothing to play.
/// </para>
/// <para>
/// The callback form requires <see cref="AudioSystem.Initialize"/> to have been called (the
/// callback's format is the pinned device format). The <see cref="ISampleProvider"/> form
/// uses the provider's own declared format, which must match the device rate.
/// </para>
/// </remarks>
public sealed class StreamingAudioSource : IDisposable
{
    private readonly WaveOutEvent _output = new();
    private readonly VolumeSampleProvider _volumeProvider;
    private bool _isDisposed;

    /// <summary>
    /// Creates a streaming source fed by <paramref name="fillBuffer"/> at the pinned device
    /// format. <see cref="AudioSystem.Initialize"/> must have been called first.
    /// </summary>
    public StreamingAudioSource(FillAudioBuffer fillBuffer)
        : this(new CallbackSampleProvider(
            fillBuffer ?? throw new ArgumentNullException(nameof(fillBuffer)),
            AudioSystem.IsInitialized
                ? WaveFormat.CreateIeeeFloatWaveFormat(AudioSystem.DeviceSampleRate, AudioSystem.DeviceChannels)
                : throw new InvalidOperationException(
                    $"Call {nameof(AudioSystem)}.{nameof(AudioSystem.Initialize)}() before creating a callback-based {nameof(StreamingAudioSource)}, so the callback's sample format is defined.")))
    {
    }

    /// <summary>
    /// Creates a streaming source fed by <paramref name="source"/>, an endless provider whose
    /// declared format must match the device rate.
    /// </summary>
    public StreamingAudioSource(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _volumeProvider = new VolumeSampleProvider(source);
        _output.Init(_volumeProvider);
    }

    /// <summary>The stream volume, 0.0 (silent) to 1.0 (full). May be changed while playing.</summary>
    public float Volume
    {
        get => _volumeProvider.Volume;
        set => _volumeProvider.Volume = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>True while the stream voice is playing.</summary>
    public bool IsPlaying => _output.PlaybackState == PlaybackState.Playing;

    /// <summary>Starts (or resumes) pulling and playing the stream.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _output.Play();
    }

    /// <summary>Stops the stream voice; <see cref="Start"/> starts it again.</summary>
    public void Stop() => _output.Stop();

    /// <summary>Stops the stream and releases its voice.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _output.Stop();
        _output.Dispose();
    }

    private sealed class CallbackSampleProvider : ISampleProvider
    {
        private readonly FillAudioBuffer _fillBuffer;

        public CallbackSampleProvider(FillAudioBuffer fillBuffer, WaveFormat waveFormat)
        {
            _fillBuffer = fillBuffer;
            WaveFormat = waveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            _fillBuffer(buffer);
            return buffer.Length; // endless: the callback always fills the whole buffer
        }
    }
}
