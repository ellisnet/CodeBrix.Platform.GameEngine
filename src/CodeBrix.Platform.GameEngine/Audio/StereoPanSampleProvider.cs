using CodeBrix.Audio.Wave; //was previously: using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Audio; //was previously: Gondwana.Audio;
/// <summary>
/// Simple stereo balancer: multiplies L/R samples independently.
/// Expects stereo IEEE float (the audio pipeline converts upstream via ToSampleProvider()).
/// </summary>
public sealed class StereoPanSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    /// <summary>
    /// Initializes a new instance of the <see cref="StereoPanSampleProvider"/> class with the specified stereo sample source.
    /// </summary>
    /// <param name="source">The stereo sample source to apply panning to. Must be stereo (2 channels) and use IEEE float encoding.</param>
    /// <exception cref="ArgumentException">Thrown when the source is not stereo (2 channels) or does not use IEEE float encoding.</exception>
    public StereoPanSampleProvider(ISampleProvider source)
    {
        if (source.WaveFormat.Channels != 2)
            throw new ArgumentException("StereoPanSampleProvider requires a stereo source.");

        if (source.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            throw new ArgumentException("StereoPanSampleProvider requires IEEE float samples (use ToSampleProvider first).");

        _source = source;
        LeftVolume = 1f;
        RightVolume = 1f;
    }

    /// <summary>
    /// Gets or sets the volume multiplier for the left channel.
    /// </summary>
    /// <remarks>A value of 1.0 represents full volume. Values between 0.0 and 1.0 reduce volume, while values greater than 1.0 can provide gain.</remarks>
    public float LeftVolume { get; set; }  // 0..1 (you can allow >1 if you want gain)

    /// <summary>
    /// Gets or sets the volume multiplier for the right channel.
    /// </summary>
    /// <remarks>A value of 1.0 represents full volume. Values between 0.0 and 1.0 reduce volume, while values greater than 1.0 can provide gain.</remarks>
    public float RightVolume { get; set; }

    /// <summary>
    /// Gets the wave format of the sample provider.
    /// </summary>
    /// <remarks>Returns the wave format from the underlying source provider.</remarks>
    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// Reads samples from the source and applies independent left and right channel volume adjustments.
    /// </summary>
    /// <param name="buffer">The buffer to fill with interleaved stereo samples.</param>
    /// <returns>The number of samples actually read from the source.</returns>
    public int Read(Span<float> buffer)
    {
        // buffer length is the number of floats (interleaved stereo)
        int read = _source.Read(buffer);
        // apply per-channel gain
        for (int i = 0; i < read; i += 2)
        {
            buffer[i] *= LeftVolume;  // L
            if (i + 1 < read)
                buffer[i + 1] *= RightVolume; // R
        }
        return read;
    }
}