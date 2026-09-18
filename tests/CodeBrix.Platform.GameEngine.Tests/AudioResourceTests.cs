using System;
using System.IO;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="AudioResource.PlaybackSpeed"/>: its clamped range, the variable-rate stage it
/// drives when the resource's graph carries one, and its survival across a clone.
/// </summary>
/// <remarks>
/// NOTHING HERE IS PLAYED, but loading a resource still builds an output voice, and that makes the
/// process-wide shared output ADOPT A SAMPLE RATE. Left alone that rate outlives these tests and
/// fails later ones whose source has a different rate, so the audio system is shut down after each
/// test.
/// </remarks>
public class AudioResourceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "codebrix-gameengine-audioresource-" + Guid.NewGuid().ToString("N"));

    public AudioResourceTests()
    {
        Directory.CreateDirectory(_directory);
        AudioResourceManager.Instance.Clear();
    }

    /// <summary>
    /// Unloads everything these tests loaded and un-claims the shared audio output, so the sample
    /// rate a resource adopted here does not leak into the rest of the suite.
    /// </summary>
    public void Dispose()
    {
        AudioResourceManager.Instance.Clear();
        AudioSystem.Shutdown();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public void PlaybackSpeed_defaults_to_normal_speed()
    {
        //Arrange
        var resource = AudioResourceManager.Instance.LoadFromPcm("speed_default", new byte[] { 0x80, 0x90 }, 11025, 8);

        //Act
        var speed = resource.PlaybackSpeed;

        //Assert
        speed.Should().Be(1f);
    }

    [Theory]
    [InlineData(0f, AudioResource.MinimumPlaybackSpeed)]
    [InlineData(0.1f, AudioResource.MinimumPlaybackSpeed)]
    [InlineData(-3f, AudioResource.MinimumPlaybackSpeed)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(2f, 2f)]
    [InlineData(9f, AudioResource.MaximumPlaybackSpeed)]
    [InlineData(float.PositiveInfinity, AudioResource.MaximumPlaybackSpeed)]
    public void PlaybackSpeed_clamps_to_the_supported_range(float requested, float expected)
    {
        //Arrange
        var resource = AudioResourceManager.Instance.LoadFromPcm("speed_clamp", new byte[] { 0x80, 0x90 }, 11025, 8);

        //Act
        resource.PlaybackSpeed = requested;

        //Assert
        resource.PlaybackSpeed.Should().Be(expected);
    }

    [Fact]
    public void PlaybackSpeed_rejects_not_a_number()
    {
        //Arrange
        var resource = AudioResourceManager.Instance.LoadFromPcm("speed_nan", new byte[] { 0x80, 0x90 }, 11025, 8);

        //Act
        var act = () => resource.PlaybackSpeed = float.NaN;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        resource.PlaybackSpeed.Should().Be(1f);
    }

    [Fact]
    public void PlaybackSpeed_drives_the_rate_stage_when_the_graph_carries_one()
    {
        //Arrange - a pinned device plus a source at a different rate is exactly the case where the
        //resource builds a variable-rate stage, which is what the speed is applied to.
        AudioSystem.Initialize(44100, 2);
        var resource = AudioResourceManager.Instance.LoadFromPcm("speed_rate_stage", new byte[] { 0x80, 0x90, 0xA0, 0xB0 }, 11025, 8);
        var stage = GetRateStage(resource);
        stage.Should().NotBeNull();

        //Act
        resource.PlaybackSpeed = 2f;

        //Assert
        stage!.Pitch.Should().Be(2f);
        resource.PlaybackSpeed.Should().Be(2f);
    }

    [Fact]
    public void PlaybackSpeed_drives_the_rate_stage_without_a_pinned_device_too()
    {
        //Arrange - no pinned device: the graph still carries the rate stage, at the source's own
        //rate, so the speed applies to every loaded resource rather than only converting ones.
        var resource = AudioResourceManager.Instance.LoadFromPcm("speed_unpinned", BuildPcmLump(4096), 11025, 8);
        var stage = GetRateStage(resource);
        stage.Should().NotBeNull();
        stage!.WaveFormat.SampleRate.Should().Be(11025);

        //Act
        resource.PlaybackSpeed = 0.5f;

        //Assert
        stage.Pitch.Should().Be(0.5f);
        resource.PlaybackSpeed.Should().Be(0.5f);
    }

    [Fact]
    public void CurrentTime_follows_the_samples_handed_over_at_the_default_speed()
    {
        //Arrange - one second of mono 8-bit PCM at 11025 Hz, played at the default speed.
        const int sampleRate = 11025;
        var resource = AudioResourceManager.Instance.LoadFromPcm("unity_position", BuildPcmLump(sampleRate), sampleRate, 8);
        var stage = GetRateStage(resource);
        stage.Should().NotBeNull();
        var frameSeconds = 1.0 / sampleRate;

        //Act - pull a quarter of a second through the graph, as the output device does.
        const int framesPulled = 2756;
        stage!.Read(new float[framesPulled]).Should().Be(framesPulled);

        //Assert - the stream sits exactly where the samples that were handed over left it: the
        //rate stage reads no further ahead than it was asked for, so reported position and
        //duration stay truthful.
        (Math.Abs(resource.CurrentTime.TotalSeconds - framesPulled * frameSeconds) <= frameSeconds)
            .Should().BeTrue($"expected about {framesPulled * frameSeconds:0.0000} s but got {resource.CurrentTime.TotalSeconds:0.0000} s");
        (Math.Abs(resource.Duration.TotalSeconds - 1.0) <= frameSeconds).Should().BeTrue();
    }

    [Fact]
    public void PlaybackSpeed_is_carried_over_to_a_clone()
    {
        //Arrange - a short .wav preloads, so the clone shares the decoded PCM (the cached branch).
        var wavPath = WriteWav("clone_source.wav");
        var original = AudioResourceManager.Instance.LoadFromFile("speed_clone_source", wavPath);
        original.PlaybackSpeed = 1.75f;

        //Act
        var clone = AudioResourceManager.Instance.Clone("speed_clone_source", "speed_clone_copy");

        //Assert
        clone.Should().NotBeNull();
        clone!.PlaybackSpeed.Should().Be(1.75f);
    }

    [Fact]
    public void Play_re_arms_the_rate_stage_so_a_looping_resource_sounds_again()
    {
        //Arrange - a looping resource whose graph carries the rate stage, drained to the end of
        //its source exactly as the audio thread drains it at a loop boundary.
        AudioSystem.Initialize(44100, 2);
        var resource = AudioResourceManager.Instance.LoadFromPcm("loop_restart", BuildPcmLump(11025), 11025, 8);
        resource.IsLooping = true;
        resource.Volume = 0f; // this test starts the voice; it does not need to be audible
        var stage = GetRateStage(resource);
        stage.Should().NotBeNull();
        DrainToEndOfSource(stage!);
        SourceHasEnded(stage!).Should().BeTrue();

        //Act - the restart the looping path performs when playback completes.
        resource.Play(fromStart: true);

        //Assert - the stage reads the rewound stream again instead of ending instantly, which is
        //what turned looping into a silent stop/start cycle.
        SourceHasEnded(stage!).Should().BeFalse();
        resource.Stop();
    }

    [Fact]
    public void Seek_re_arms_the_rate_stage()
    {
        //Arrange - drained to the end of the source, with nothing playing.
        AudioSystem.Initialize(44100, 2);
        var resource = AudioResourceManager.Instance.LoadFromPcm("seek_restart", BuildPcmLump(4096), 11025, 8);
        var stage = GetRateStage(resource);
        stage.Should().NotBeNull();
        DrainToEndOfSource(stage!);

        //Act
        resource.Seek(TimeSpan.Zero);

        //Assert - audio flows again from the new position.
        var buffer = new float[512];
        stage!.Read(buffer).Should().BeGreaterThan(0);
    }

    private static byte[] BuildPcmLump(int sampleCount)
    {
        // 8-bit unsigned PCM: a slow ramp around the 0x80 silence point.
        var data = new byte[sampleCount];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(0x80 + (i % 32) - 16);
        }

        return data;
    }

    private static void DrainToEndOfSource(VariableRateSampleProvider stage)
    {
        var sink = new float[4096];
        while (stage.Read(sink) > 0)
        {
            // Read to the end, the way the audio thread does.
        }
    }

    private static bool SourceHasEnded(VariableRateSampleProvider stage)
    {
        var field = typeof(VariableRateSampleProvider).GetField("_sourceEnded", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (bool)field!.GetValue(stage)!;
    }

    private static VariableRateSampleProvider? GetRateStage(AudioResource resource)
    {
        // The rate stage is a private graph detail; reaching it by reflection keeps it that way
        // while still proving the property is wired to it.
        var field = typeof(AudioResource).GetField("rateProvider", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (VariableRateSampleProvider?)field!.GetValue(resource);
    }

    private string WriteWav(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        const int sampleRate = 8000;
        const int sampleCount = 80;
        int dataLength = sampleCount * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        for (int i = 0; i < sampleCount; i++)
        {
            writer.Write((short)(i * 100));
        }

        return path;
    }
}
