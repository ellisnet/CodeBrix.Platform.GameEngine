using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Opus;
using CodeBrix.Audio.Wave;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="PlatformAudioFactory"/>'s two-step resolution: the engine's own registrations
/// first, then CodeBrix.Audio's <see cref="AudioFileReaderRegistry"/>. Nothing here opens an audio
/// device — the factory is asked for readers directly.
/// </summary>
/// <remarks>
/// These tests reach the real Opus codec, which this test project references and the shipped engine
/// package deliberately does not (Opus is BSD-3-Clause; the engine is MIT). That is the whole point
/// of the design being tested: the engine gains the format without gaining the dependency.
/// </remarks>
public class PlatformAudioFactoryTests
{
    [Theory]
    [InlineData("music.wav")]
    [InlineData("music.mp3")]
    [InlineData("music.ogg")]
    [InlineData("music.flac")]
    public void PlatformAudioFactory_supports_the_built_in_formats(string fileName)
    {
        //Arrange & Act
        var supported = PlatformAudioFactory.Supports(fileName);

        //Assert
        supported.Should().BeTrue();
    }

    [Fact]
    public void PlatformAudioFactory_reports_an_unknown_format_as_unsupported()
    {
        //Arrange & Act
        var supported = PlatformAudioFactory.Supports("music.nosuchformat");

        //Assert
        supported.Should().BeFalse();
    }

    [Fact]
    public void PlatformAudioFactory_message_for_an_unknown_format_lists_what_is_registered()
    {
        //Arrange & Act
        var act = () => PlatformAudioFactory.GetReaderFactory("music.nosuchformat");

        //Assert
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*.wav*");
    }

    [Fact]
    public void PlatformAudioFactory_resolves_a_format_registered_only_with_the_CodeBrix_Audio_registry()
    {
        //Arrange - the seam an add-on codec package uses. A game never writes this itself; the
        // package's own Register() call does, which is what the Opus test below exercises for real.
        const string extension = ".gameenginebridgetest";
        AudioFileReaderRegistry.Register(extension, stream => new WaveFileReader(stream));

        //Act
        var supported = PlatformAudioFactory.Supports("clip" + extension);
        var listed = PlatformAudioFactory.SupportedExtensions().Contains(extension);
        var (factory, requiresFile) = PlatformAudioFactory.GetReaderFactory("clip" + extension);

        //Assert
        supported.Should().BeTrue();
        listed.Should().BeTrue();
        factory.Should().NotBeNull();
        requiresFile.Should().BeFalse("a registry factory takes a stream by contract");
    }

    [Fact]
    public void PlatformAudioFactory_own_registration_wins_over_the_registry()
    {
        //Arrange - both layers claim the same extension, with distinguishable readers.
        const string extension = ".gameengineprecedencetest";
        AudioFileReaderRegistry.Register(extension, _ => throw new InvalidOperationException("registry reader"));
        PlatformAudioFactory.Register(extension, _ => throw new NotImplementedException("engine reader"));

        //Act
        var (factory, _) = PlatformAudioFactory.GetReaderFactory("clip" + extension);
        var act = () => factory(Stream.Null);

        //Assert
        act.Should().Throw<NotImplementedException>("the engine's own registration takes precedence");
    }

    [Fact]
    public void PlatformAudioFactory_carries_the_requires_file_flag_on_its_own_registrations()
    {
        //Arrange - the one thing the CodeBrix.Audio registry has no concept of, which is why the
        // engine keeps a table of its own rather than delegating entirely.
        const string extension = ".gameenginerequiresfiletest";
        PlatformAudioFactory.Register(extension, stream => new WaveFileReader(stream), requiresFile: true);

        //Act
        var (_, requiresFile) = PlatformAudioFactory.GetReaderFactory("clip" + extension);

        //Assert
        requiresFile.Should().BeTrue();
    }

    [Fact]
    public void Opus_is_unsupported_until_registered_and_then_loads_like_any_other_format()
    {
        //Arrange - the whole Opus story in one test, in order, because CodeBrixAudioOpus.Register()
        // is process-wide and permanent: asserting the "before" state in a separate test would make
        // the two order-dependent.
        const string fileName = "tone.opus";

        //Act - BEFORE registration: unsupported, with a message that names the fix.
        var supportedBefore = PlatformAudioFactory.Supports(fileName);
        var beforeRegistration = () => PlatformAudioFactory.GetReaderFactory(fileName);

        //Assert
        supportedBefore.Should().BeFalse();
        beforeRegistration.Should().Throw<NotSupportedException>()
            .WithMessage("*CodeBrixAudioOpus.Register()*");

        //Act - the one call an application makes.
        CodeBrixAudioOpus.Register();

        //Assert - .opus is now a first-class engine format: it resolves, it is listed, and a real
        // Opus stream decodes through the engine's own loading path.
        PlatformAudioFactory.Supports(fileName).Should().BeTrue();
        PlatformAudioFactory.SupportedExtensions().Should().Contain(".opus");

        var (factory, requiresFile) = PlatformAudioFactory.GetReaderFactory(fileName);
        requiresFile.Should().BeFalse();

        using var reader = factory(new MemoryStream(BuildOpusTone()));
        reader.WaveFormat.SampleRate.Should().Be(48000, "Opus always decodes at 48 kHz");
        reader.TotalTime.TotalSeconds.Should().BeApproximately(0.5, 0.05);
    }

    [Fact]
    public void An_opus_clip_preloads_to_PCM_exactly_like_the_built_in_formats()
    {
        //Arrange - the "treated equally, and optimizable" claim: the preload-to-PCM path that
        // SfxVoicePool plays from must work for a format the engine does not itself carry.
        CodeBrixAudioOpus.Register();
        var (factory, _) = PlatformAudioFactory.GetReaderFactory("tone.opus");

        //Act
        using var reader = factory(new MemoryStream(BuildOpusTone()));
        var cached = new CachedSound(reader);

        //Assert
        cached.AudioData.Length.Should().BeGreaterThan(0);
        cached.SampleRate.Should().Be(48000);
        cached.Duration.TotalSeconds.Should().BeApproximately(0.5, 0.05);
        cached.AudioData.Any(sample => Math.Abs(sample) > 0.1f)
            .Should().BeTrue("the decoded tone should not be silence");
    }

    /// <summary>
    /// Half a second of a 440 Hz tone as a real Ogg Opus stream, written by the Opus package's own
    /// writer. Building the fixture in code rather than committing one keeps a binary asset out of
    /// the repo and makes the test prove the encode and decode halves together.
    /// </summary>
    private static byte[] BuildOpusTone()
    {
        const int sampleRate = 48000;
        const int frames = sampleRate / 2;

        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / sampleRate);
        }

        // The Stream overload does not take ownership, so disposing the writer finalises the Ogg
        // stream (which it only does on Dispose) without closing the MemoryStream underneath it.
        var stream = new MemoryStream();
        using (var writer = new OpusFileWriter(stream, sampleRate, channels: 1))
        {
            writer.Write(samples, 0, samples.Length);
        }

        return stream.ToArray();
    }
}
