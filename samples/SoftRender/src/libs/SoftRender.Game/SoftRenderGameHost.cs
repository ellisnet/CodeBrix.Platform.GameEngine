using System;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Host.Hosting;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Input.Keyboard;
using CodeBrix.Platform.GameEngine.Rendering;
using Windows.System;

namespace SoftRender.Game;

/// <summary>
/// The SoftRender demo: a classic software-rendered plasma with a drifting starfield and a
/// keyboard-steered cursor, rendered on the CPU into a 320x200 framebuffer at 70 Hz and
/// presented through <see cref="PixelFramePresenter"/> — plus a raw-PCM key blip on a
/// <see cref="SoundChannel"/> (with random pitch) and a procedurally streamed drone on a
/// <see cref="StreamingAudioSource"/> (Space toggles it). This exercises the engine's whole
/// software-rendered enablement stack: presenter, fixed-rate loop, input pump, and audio.
/// </summary>
public sealed class SoftRenderGameHost : SoftwareRenderedGameHostBase
{
    private const int FrameWidth = 320;
    private const int FrameHeight = 200;
    private const int TicRate = 70;
    private const int StarCount = 96;

    // Plasma lookups, precomputed once (the render path is allocation-free).
    private readonly byte[] _distanceTable = new byte[FrameWidth * FrameHeight];
    private readonly byte[] _angleWave = new byte[FrameWidth * FrameHeight];
    private readonly uint[] _palette = new uint[256];

    private readonly float[] _starX = new float[StarCount];
    private readonly float[] _starY = new float[StarCount];
    private readonly float[] _starSpeed = new float[StarCount];

    private SoundChannel _blipChannel;
    private StreamingAudioSource _drone;
    private double _dronePhase;
    private bool _droneOn;

    private int _tic;
    private float _cursorX = FrameWidth / 2f;
    private float _cursorY = FrameHeight / 2f;
    private long _lastStatsTic;
    private long _lastStatsAllocatedBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftRenderGameHost"/> class.
    /// </summary>
    /// <param name="canvas">The render surface to present into.</param>
    public SoftRenderGameHost(GameSurfaceCanvas canvas)
        : base(canvas, TicRate)
    {
    }

    /// <inheritdoc />
    protected override void ConfigureAudio() => AudioSystem.Initialize(44100, 2);

    /// <inheritdoc />
    protected override void OnLoadContent()
    {
        Presenter.Configure(FrameWidth, FrameHeight, PixelBufferFormat.Rgba8888);

        BuildPlasmaTables();
        var random = new Random(20260717);
        for (var i = 0; i < StarCount; i++)
        {
            _starX[i] = random.Next(FrameWidth);
            _starY[i] = random.Next(FrameHeight);
            _starSpeed[i] = 0.3f + 1.7f * random.NextSingle();
        }

        // A raw-PCM blip lump: 8-bit unsigned mono at 11025 Hz (the classic game SFX
        // format), rate-converted and pitched by the SoundChannel at play time.
        AudioResourceManager.Instance.LoadFromPcm("softrender_blip", BuildBlipPcm(), 11025, 8);
        _blipChannel = new SoundChannel();
        _blipChannel.SetClip("softrender_blip");

        // An endless pull-model stream: a soft two-oscillator drone synthesized in the
        // fill callback on the audio thread.
        _drone = new StreamingAudioSource(FillDroneBuffer) { Volume = 0.25f };

        // Key events by numeric key code: any key blips (random pitch), Space toggles the drone.
        KeyboardEventPoller.Instance!.StartMonitoringAllKeys();
        KeyboardEventPoller.Instance.KeyDown += OnKeyEvent;

        Console.WriteLine("SoftRender: 320x200 @ 70 Hz — arrows steer, any key blips, Space toggles the drone.");
    }

    /// <inheritdoc />
    protected override void OnTic()
    {
        _tic++;

        // Polled per-tic input (IKeyboardAdapter.IsDown is lock-free from the game thread).
        var keyboard = KeyboardEventPoller.Instance!.Adapter!;
        const float speed = 1.8f;
        if (keyboard.IsDown((int)VirtualKey.Left))
        {
            _cursorX = Math.Max(2, _cursorX - speed);
        }
        if (keyboard.IsDown((int)VirtualKey.Right))
        {
            _cursorX = Math.Min(FrameWidth - 3, _cursorX + speed);
        }
        if (keyboard.IsDown((int)VirtualKey.Up))
        {
            _cursorY = Math.Max(2, _cursorY - speed);
        }
        if (keyboard.IsDown((int)VirtualKey.Down))
        {
            _cursorY = Math.Min(FrameHeight - 3, _cursorY + speed);
        }

        for (var i = 0; i < StarCount; i++)
        {
            _starX[i] += _starSpeed[i];
            if (_starX[i] >= FrameWidth)
            {
                _starX[i] -= FrameWidth;
            }
        }

        if (_tic - _lastStatsTic >= TicRate * 5)
        {
            _lastStatsTic = _tic;
            // gameAlloc = bytes allocated on THIS (game loop) thread since the last stats
            // line: the tic/render/present path is designed to allocate zero steady-state.
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var allocatedDelta = allocated - _lastStatsAllocatedBytes;
            _lastStatsAllocatedBytes = allocated;
            Console.WriteLine(
                $"SoftRender: tics={GameLoop.TicCount} actualHz={GameLoop.ActualTicsPerSecond:F1} dropped={GameLoop.DroppedTics} gc0={GC.CollectionCount(0)} gameAlloc={allocatedDelta}B");
        }
    }

    /// <inheritdoc />
    protected override void OnRenderFrame(Span<byte> frameBuffer)
    {
        var pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(frameBuffer);

        // Classic palette-cycled plasma from the precomputed tables.
        var shift = _tic;
        for (var i = 0; i < pixels.Length; i++)
        {
            var color = (byte)(_distanceTable[i] + _angleWave[i] + shift);
            pixels[i] = _palette[color];
        }

        // Starfield over the plasma.
        for (var i = 0; i < StarCount; i++)
        {
            var brightness = (byte)(128 + (int)(127 * _starSpeed[i] / 2f));
            pixels[(int)_starY[i] * FrameWidth + (int)_starX[i]] = Pack(brightness, brightness, 0xFF);
        }

        // The keyboard-steered cursor: a small white cross.
        var cx = (int)_cursorX;
        var cy = (int)_cursorY;
        for (var offset = -2; offset <= 2; offset++)
        {
            pixels[cy * FrameWidth + cx + offset] = Pack(0xFF, 0xFF, 0xFF);
            pixels[(cy + offset) * FrameWidth + cx] = Pack(0xFF, 0xFF, 0xFF);
        }
    }

    /// <inheritdoc />
    protected override void OnShutdown()
    {
        if (KeyboardEventPoller.Instance is { } poller)
        {
            poller.KeyDown -= OnKeyEvent;
        }

        _drone?.Dispose();
        _blipChannel?.Dispose();
        AudioSystem.Shutdown();
    }

    private void OnKeyEvent(KeyDownEventArgs args)
    {
        if (args.KeyAction != KeyAction.Pressed)
        {
            return;
        }

        if (args.KeyCode == (int)VirtualKey.Space)
        {
            _droneOn = !_droneOn;
            if (_droneOn)
            {
                _drone?.Start();
            }
            else
            {
                _drone?.Stop();
            }
            return;
        }

        // Random pitch variation, the classic ±20% arcade treatment.
        _blipChannel?.Play(volume: 0.7f, pan: (_cursorX / FrameWidth) * 2f - 1f, pitch: 0.8f + 0.4f * Random.Shared.NextSingle());
    }

    private void FillDroneBuffer(Span<float> buffer)
    {
        // Two detuned sine oscillators at the pinned device rate, interleaved stereo.
        const double baseFrequency = 110.0;
        var step = 2 * Math.PI * baseFrequency / AudioSystem.DeviceSampleRate;
        for (var i = 0; i < buffer.Length; i += 2)
        {
            var sample = (float)(0.5 * Math.Sin(_dronePhase) + 0.5 * Math.Sin(_dronePhase * 1.007));
            buffer[i] = sample;
            buffer[i + 1] = sample;
            _dronePhase += step;
        }

        if (_dronePhase > 2 * Math.PI * baseFrequency)
        {
            _dronePhase -= 2 * Math.PI * baseFrequency;
        }
    }

    private void BuildPlasmaTables()
    {
        for (var y = 0; y < FrameHeight; y++)
        {
            for (var x = 0; x < FrameWidth; x++)
            {
                var dx = x - FrameWidth / 2.0;
                var dy = (y - FrameHeight / 2.0) * 1.6; // compensate the 320x200 pixel aspect
                var distance = Math.Sqrt(dx * dx + dy * dy);
                _distanceTable[y * FrameWidth + x] = (byte)(distance * 2.5);
                _angleWave[y * FrameWidth + x] = (byte)(128 + 64 * Math.Sin(x / 24.0) + 64 * Math.Sin(y / 16.0));
            }
        }

        for (var i = 0; i < 256; i++)
        {
            var angle = i * Math.PI / 128;
            var r = (byte)(128 + 127 * Math.Sin(angle));
            var g = (byte)(128 + 127 * Math.Sin(angle + 2 * Math.PI / 3));
            var b = (byte)(128 + 127 * Math.Sin(angle + 4 * Math.PI / 3));
            _palette[i] = Pack(r, g, b);
        }
    }

    private static byte[] BuildBlipPcm()
    {
        // ~120 ms of a decaying 440 Hz square wave, 8-bit unsigned mono at 11025 Hz.
        const int sampleRate = 11025;
        var samples = new byte[sampleRate * 120 / 1000];
        for (var i = 0; i < samples.Length; i++)
        {
            var envelope = 1.0 - (double)i / samples.Length;
            var square = Math.Sin(2 * Math.PI * 440 * i / sampleRate) >= 0 ? 1 : -1;
            samples[i] = (byte)(0x80 + 96 * envelope * square);
        }

        return samples;
    }

    /// <summary>Packs RGB into an RGBA8888 memory-order pixel (little-endian uint).</summary>
    private static uint Pack(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16) | (0xFF << 24));
}
