using System;
using System.Threading;
using CodeBrix.Platform.GameEngine.Audio;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// A scriptable <see cref="IStreamingMusicProvider"/>: it can start late (a number of renders of
/// nothing before audio), stall mid-stream, fault the way the contract says, break the contract by
/// throwing, hand back short renders, and otherwise produce a known tone (left = a sine, right = its
/// negation, so the two planes are told apart). It records every call the engine makes.
/// </summary>
internal sealed class FakeStreamingMusicProvider : IStreamingMusicProvider
{
    private volatile StreamingMusicState _state = StreamingMusicState.Stopped;
    private int _silentRendersLeft;
    private long _framesRendered;
    private int _inRender;

    internal FakeStreamingMusicProvider(string name = "Fake music") => Name = name;

    public string Name { get; }

    public string Description { get; set; } = "a known tone";

    public StreamingMusicState State => _state;

    public Exception? Fault { get; private set; }

    public event EventHandler? StateChanged;

    /// <summary>Renders that return nothing (state Starting) after each start, before audio arrives.</summary>
    internal int SilentRendersBeforeAudio { get; set; }

    /// <summary>After this many frames the provider stalls (state Starved) until <see cref="EndStall"/>; -1 never.</summary>
    internal long StallAfterFrames { get; set; } = -1;

    /// <summary>The next render faults: state Faulted, <see cref="Fault"/> set, nothing thrown.</summary>
    internal bool FaultOnNextRender { get; set; }

    /// <summary>Every render throws — a provider breaking the contract.</summary>
    internal bool ThrowOnRender { get; set; }

    /// <summary>The most frames one render hands back.</summary>
    internal int MaxFramesPerRender { get; set; } = int.MaxValue;

    internal double ToneHz { get; set; } = 441.0;

    internal float Amplitude { get; set; } = 0.5f;

    internal int StartCalls { get; private set; }

    internal int StopCalls { get; private set; }

    internal int RenderCalls { get; private set; }

    internal int LastSampleRate { get; private set; }

    internal int LastChannels { get; private set; }

    internal bool Disposed { get; private set; }

    internal bool ConcurrentRenderSeen { get; private set; }

    internal bool RenderedWhileStopped { get; private set; }

    internal long FramesRendered => Interlocked.Read(ref _framesRendered);

    public void Start(int sampleRate, int channels)
    {
        StartCalls++;
        LastSampleRate = sampleRate;
        LastChannels = channels;
        Interlocked.Exchange(ref _framesRendered, 0);
        _silentRendersLeft = SilentRendersBeforeAudio;
        Fault = null;
        SetState(_silentRendersLeft > 0 ? StreamingMusicState.Starting : StreamingMusicState.Playing);
    }

    public void Stop()
    {
        StopCalls++;
        if (_state != StreamingMusicState.Stopped)
        {
            SetState(StreamingMusicState.Stopped);
        }
    }

    public int Render(Span<float> left, Span<float> right)
    {
        if (Interlocked.Increment(ref _inRender) > 1)
        {
            ConcurrentRenderSeen = true;
        }

        try
        {
            RenderCalls++;

            if (ThrowOnRender)
            {
                throw new InvalidOperationException("The fake provider was told to throw.");
            }

            if (_state == StreamingMusicState.Stopped)
            {
                RenderedWhileStopped = true;
                return 0;
            }

            if (_state == StreamingMusicState.Faulted)
            {
                return 0;
            }

            if (FaultOnNextRender)
            {
                FaultOnNextRender = false;
                Fault = new InvalidOperationException("The fake provider faulted.");
                SetState(StreamingMusicState.Faulted);
                return 0;
            }

            if (_silentRendersLeft > 0)
            {
                _silentRendersLeft--;
                return 0;
            }

            var rendered = FramesRendered;
            if (StallAfterFrames >= 0 && rendered >= StallAfterFrames)
            {
                if (_state != StreamingMusicState.Starved)
                {
                    SetState(StreamingMusicState.Starved);
                }

                return 0;
            }

            if (_state != StreamingMusicState.Playing)
            {
                SetState(StreamingMusicState.Playing);
            }

            var frames = Math.Min(left.Length, MaxFramesPerRender);
            if (StallAfterFrames >= 0)
            {
                frames = (int)Math.Min(frames, StallAfterFrames - rendered);
            }

            for (var i = 0; i < frames; i++)
            {
                var sample = ExpectedLeft(rendered + i);
                left[i] = sample;
                right[i] = -sample;
            }

            Interlocked.Add(ref _framesRendered, frames);

            if (StallAfterFrames >= 0 && FramesRendered >= StallAfterFrames && frames < left.Length)
            {
                SetState(StreamingMusicState.Starved); // ran dry part-way through this render
            }

            return frames;
        }
        finally
        {
            Interlocked.Decrement(ref _inRender);
        }
    }

    /// <summary>The left-channel sample the tone has at a frame of the current timeline.</summary>
    internal float ExpectedLeft(long frame)
        => (float)(Amplitude * Math.Sin(2.0 * Math.PI * ToneHz * frame / Math.Max(1, LastSampleRate)));

    /// <summary>Lets a stalled stream carry on.</summary>
    internal void EndStall() => StallAfterFrames = -1;

    /// <summary>Changes state from outside the render path, as a provider ending by itself would.</summary>
    internal void SetState(StreamingMusicState state)
    {
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Disposed = true;
}
