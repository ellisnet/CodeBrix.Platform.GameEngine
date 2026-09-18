using System;
using System.Reflection;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="SoundChannel"/>'s replay behaviour: a channel is meant to be re-triggered
/// constantly, so the rate/pitch stage behind it has to be re-armed every time the clip is rewound.
/// </summary>
/// <remarks>
/// A channel requires <see cref="AudioSystem.Initialize"/>, which makes the process-wide shared
/// output ADOPT A SAMPLE RATE, so the audio system is shut down after each test. Playback is started
/// at zero volume: the tests are about the sample pipeline, not about making a sound.
/// </remarks>
public class SoundChannelTests : IDisposable
{
    private const string ClipKey = "sound_channel_clip";

    public SoundChannelTests()
    {
        AudioResourceManager.Instance.Clear();
        AudioSystem.Initialize(44100, 2);
    }

    public void Dispose()
    {
        AudioResourceManager.Instance.Clear();
        AudioSystem.Shutdown();
    }

    [Fact]
    public void Play_re_arms_the_rate_stage_so_a_replayed_clip_sounds_again()
    {
        //Arrange - a channel whose clip has already run all the way to the end, which is what
        //latched the rate stage and left every later play silent.
        AudioResourceManager.Instance.LoadFromPcm(ClipKey, BuildPcmLump(220500), 11025, 8);
        using var channel = new SoundChannel();
        channel.SetClip(ClipKey);
        channel.Volume = 0f;

        var stage = GetRateStage(channel);
        stage.Should().NotBeNull();
        DrainToEndOfSource(stage!);
        SourceHasEnded(stage!).Should().BeTrue();

        //Act
        channel.Play();

        //Assert - the stage reads the rewound clip again instead of ending instantly.
        SourceHasEnded(stage!).Should().BeFalse();
        channel.Stop();
    }

    [Fact]
    public void SetClip_builds_a_rate_stage_at_the_channel_pitch()
    {
        //Arrange
        AudioResourceManager.Instance.LoadFromPcm(ClipKey, BuildPcmLump(4096), 11025, 8);
        using var channel = new SoundChannel();

        //Act
        channel.Pitch = 1.5f;
        channel.SetClip(ClipKey);

        //Assert
        var stage = GetRateStage(channel);
        stage.Should().NotBeNull();
        stage!.Pitch.Should().Be(1.5f);
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

    private static VariableRateSampleProvider? GetRateStage(SoundChannel channel)
    {
        // The rate stage is a private pipeline detail; reaching it by reflection keeps it that way
        // while still proving the channel re-arms it.
        var field = typeof(SoundChannel).GetField("_rateProvider", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (VariableRateSampleProvider?)field!.GetValue(channel);
    }
}
