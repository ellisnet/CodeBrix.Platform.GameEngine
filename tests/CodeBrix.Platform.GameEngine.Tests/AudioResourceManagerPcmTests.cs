using System;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

public class AudioResourceManagerPcmTests
{
    [Fact]
    public void LoadFromPcm_rejects_invalid_formats()
    {
        //Arrange
        var manager = AudioResourceManager.Instance;
        var data = new byte[16];

        //Act
        Action nullData = () => manager.LoadFromPcm("pcm_bad", null!, 11025, 8);
        Action badBits = () => manager.LoadFromPcm("pcm_bad", data, 11025, 12);
        Action badChannels = () => manager.LoadFromPcm("pcm_bad", data, 11025, 8, channels: 3);
        Action badRate = () => manager.LoadFromPcm("pcm_bad", data, 0, 8);

        //Assert
        nullData.Should().Throw<ArgumentNullException>();
        badBits.Should().Throw<ArgumentOutOfRangeException>();
        badChannels.Should().Throw<ArgumentOutOfRangeException>();
        badRate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LoadFromPcm_8_bit_unsigned_decodes_to_centered_floats()
    {
        //Arrange - 8-bit unsigned: 0x80 is silence (0.0), 0x00 is -1.0, 0xFF is ~+1.0.
        var manager = AudioResourceManager.Instance;
        var data = new byte[] { 0x80, 0x00, 0xFF, 0x80 };
        const string key = "pcm_8bit_decode_test";

        try
        {
            //Act
            var resource = manager.LoadFromPcm(key, data, 11025, 8);
            using var reader = resource.CreateIndependentReader();
            var provider = reader.ToSampleProvider();
            var samples = new float[4];
            var read = provider.Read(samples);

            //Assert
            read.Should().Be(4);
            (Math.Abs(samples[0]) < 0.02f).Should().BeTrue($"0x80 should decode near 0.0 but was {samples[0]}");
            (samples[1] < -0.95f).Should().BeTrue($"0x00 should decode near -1.0 but was {samples[1]}");
            (samples[2] > 0.95f).Should().BeTrue($"0xFF should decode near +1.0 but was {samples[2]}");
            provider.WaveFormat.SampleRate.Should().Be(11025);
            provider.WaveFormat.Channels.Should().Be(1);
        }
        finally
        {
            manager.Unload(key);
        }
    }

    [Fact]
    public void LoadFromPcm_16_bit_signed_decodes_to_floats()
    {
        //Arrange - 16-bit signed little-endian: 0, short.MaxValue, short.MinValue.
        var manager = AudioResourceManager.Instance;
        var data = new byte[] { 0x00, 0x00, 0xFF, 0x7F, 0x00, 0x80 };
        const string key = "pcm_16bit_decode_test";

        try
        {
            //Act
            var resource = manager.LoadFromPcm(key, data, 22050, 16);
            using var reader = resource.CreateIndependentReader();
            var provider = reader.ToSampleProvider();
            var samples = new float[3];
            var read = provider.Read(samples);

            //Assert
            read.Should().Be(3);
            samples[0].Should().Be(0f);
            (samples[1] > 0.99f).Should().BeTrue($"short.MaxValue should decode near +1.0 but was {samples[1]}");
            (samples[2] < -0.99f).Should().BeTrue($"short.MinValue should decode near -1.0 but was {samples[2]}");
        }
        finally
        {
            manager.Unload(key);
        }
    }

    [Fact]
    public void LoadFromPcm_resources_create_independent_readers()
    {
        //Arrange
        var manager = AudioResourceManager.Instance;
        var data = new byte[] { 0x80, 0x90, 0xA0, 0xB0 };
        const string key = "pcm_independent_readers_test";

        try
        {
            var resource = manager.LoadFromPcm(key, data, 7000, 8);

            //Act - two readers advance independently.
            using var readerOne = resource.CreateIndependentReader();
            using var readerTwo = resource.CreateIndependentReader();
            var bufferOne = new float[4];
            var bufferTwo = new float[2];
            var readOne = readerOne.ToSampleProvider().Read(bufferOne);
            var readTwo = readerTwo.ToSampleProvider().Read(bufferTwo);

            //Assert
            readOne.Should().Be(4);
            readTwo.Should().Be(2);
            bufferTwo[0].Should().Be(bufferOne[0]);
            bufferTwo[1].Should().Be(bufferOne[1]);
        }
        finally
        {
            manager.Unload(key);
        }
    }
}
