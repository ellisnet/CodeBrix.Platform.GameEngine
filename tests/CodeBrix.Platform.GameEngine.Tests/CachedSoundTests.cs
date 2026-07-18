using System;
using System.IO;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

public class CachedSoundTests
{
    [Fact]
    public void CachedSound_decodes_a_wave_stream_to_floats()
    {
        //Arrange - 16-bit signed mono: 0, short.MaxValue, short.MinValue.
        var pcm = new byte[] { 0x00, 0x00, 0xFF, 0x7F, 0x00, 0x80 };
        using var reader = new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(11025, 16, 1));

        //Act
        var sound = new CachedSound(reader);

        //Assert
        sound.AudioData.Length.Should().Be(3);
        sound.AudioData[0].Should().Be(0f);
        (sound.AudioData[1] > 0.99f).Should().BeTrue($"short.MaxValue should decode near +1.0 but was {sound.AudioData[1]}");
        (sound.AudioData[2] < -0.99f).Should().BeTrue($"short.MinValue should decode near -1.0 but was {sound.AudioData[2]}");
        sound.SampleRate.Should().Be(11025);
        sound.Channels.Should().Be(1);
        (Math.Abs(sound.Duration.TotalSeconds - 3.0 / 11025) < 0.0001).Should().BeTrue();
    }

    [Fact]
    public void CachedSoundSampleProvider_readers_are_independent_and_end_with_zero()
    {
        //Arrange
        var pcm = new byte[] { 0x00, 0x00, 0xFF, 0x7F, 0x00, 0x80, 0x00, 0x00 };
        using var reader = new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(8000, 16, 1));
        var sound = new CachedSound(reader);
        var providerOne = new CachedSoundSampleProvider(sound);
        var providerTwo = new CachedSoundSampleProvider(sound);

        //Act
        var bufferOne = new float[4];
        var bufferTwo = new float[2];
        var readOne = providerOne.Read(bufferOne);
        var readTwo = providerTwo.Read(bufferTwo);
        var readAtEnd = providerOne.Read(bufferOne);

        //Assert
        readOne.Should().Be(4);
        readTwo.Should().Be(2);
        readAtEnd.Should().Be(0);
        bufferTwo[0].Should().Be(bufferOne[0]);
        bufferTwo[1].Should().Be(bufferOne[1]);
        providerTwo.SamplePosition.Should().Be(2);

        //Act again - Reset rewinds to the start.
        providerOne.Reset();
        providerOne.Read(bufferOne).Should().Be(4);
    }

    [Fact]
    public void Manager_preloads_short_wav_effects_at_load_time()
    {
        //Arrange
        var manager = AudioResourceManager.Instance;
        const string key = "cached_preload_test";

        try
        {
            //Act
            var resource = manager.LoadFromStream(key, new MemoryStream(BuildMonoWav(8000, sampleCount: 800)), ".wav");

            //Assert - a 0.1 s wav qualifies as a short effect (default ceiling 10 s).
            resource.IsPreloaded.Should().BeTrue();
            resource.CachedData!.AudioData.Length.Should().Be(800);
            (Math.Abs(resource.Duration.TotalSeconds - 0.1) < 0.001).Should().BeTrue();
        }
        finally
        {
            manager.Unload(key);
        }
    }

    [Fact]
    public void Manager_clones_share_the_preloaded_cache_without_redecoding()
    {
        //Arrange
        var manager = AudioResourceManager.Instance;
        const string key = "cached_clone_test";
        const string cloneKey = "cached_clone_test_clone";

        try
        {
            var resource = manager.LoadFromStream(key, new MemoryStream(BuildMonoWav(8000, sampleCount: 80)), ".wav");

            //Act
            var clone = manager.Clone(key, cloneKey);

            //Assert
            clone.Should().NotBeNull();
            clone!.IsPreloaded.Should().BeTrue();
            ReferenceEquals(resource.CachedData, clone.CachedData).Should().BeTrue();
        }
        finally
        {
            manager.Unload(cloneKey);
            manager.Unload(key);
        }
    }

    [Fact]
    public void Manager_preload_can_be_disabled()
    {
        //Arrange
        var manager = AudioResourceManager.Instance;
        const string key = "cached_disabled_test";
        var originalCeiling = manager.PreloadShortSoundEffectMaxSeconds;

        try
        {
            manager.PreloadShortSoundEffectMaxSeconds = 0;

            //Act
            var resource = manager.LoadFromStream(key, new MemoryStream(BuildMonoWav(8000, sampleCount: 80)), ".wav");

            //Assert
            resource.IsPreloaded.Should().BeFalse();
        }
        finally
        {
            manager.PreloadShortSoundEffectMaxSeconds = originalCeiling;
            manager.Unload(key);
        }
    }

    private static byte[] BuildMonoWav(int sampleRate, int sampleCount)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        int dataLength = sampleCount * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);          // PCM
        writer.Write((short)1);          // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);    // byte rate
        writer.Write((short)2);          // block align
        writer.Write((short)16);         // bits per sample
        writer.Write("data"u8);
        writer.Write(dataLength);
        for (int i = 0; i < sampleCount; i++)
        {
            writer.Write((short)(i * 7));
        }

        writer.Flush();
        return ms.ToArray();
    }
}
