using System;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.MuPT;
using CodeBrix.Audio.MusicGeneration.SkyTNT;
using CodeBrix.Audio.Samples.FluidR3Gm;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// OPT-IN: the two real models, SkyTNT and MuPT, each through the synthesized and the recorded
/// General MIDI libraries, producing audible music through the provider. Loading a model and running
/// it costs real time, memory and processor, so these are skipped unless the environment variable
/// <c>GENERATEDMUSIC_MODEL_TESTS</c> is <c>1</c>.
/// </summary>
/// <remarks>
/// They assert only that the music is audible and that <see cref="GeneratedMusicProvider.ActiveSource"/>
/// names the model and the library - never the samples themselves, which a synthesizer or a model
/// may change without anything being wrong.
/// </remarks>
public class ModelPlaybackOptInTests : GeneratedMusicTestContext
{
    private const string OptInVariableName = "GENERATEDMUSIC_MODEL_TESTS";

    private static readonly TimeSpan ModelTimeout = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData("SkyTNT", "ModestSynthGm", "ClubArrangement")]
    [InlineData("SkyTNT", "FluidR3Gm", "AmbientElectronica")]
    [InlineData("MuPT", "ModestSynthGm", "WaltzDuetInAMinor")]
    [InlineData("MuPT", "FluidR3Gm", "JigInD")]
    public void A_model_plays_audible_music_through_an_instrument_library(string model, string library, string preset)
    {
        //Arrange
        if (Environment.GetEnvironmentVariable(OptInVariableName) != "1")
        {
            Assert.Skip(
                $"Set the environment variable {OptInVariableName}=1 to run the real SkyTNT and MuPT models through " +
                "ModestSynthGm and FluidR3Gm. They are skipped by default because loading and running a model is slow " +
                "and memory-hungry.");
            return;
        }

        IMusicGenerator generator = model == "SkyTNT" ? SkyTNTModel.Instance : MuPTModel.Instance;
        MusicGeneratorRegistry.Register(generator);
        InstrumentLibraryRegistry.Register(FluidR3GmInstrumentLibrary.Instance);
        FluidR3GmInstrumentLibrary.IsSoundFontAvailable.Should().BeTrue("the SoundFont is copied beside the tests");
        GeneralMidiInstrumentLibrary.LibraryName.Should().Be("ModestSynthGm");

        using var provider = new GeneratedMusicProvider(new GeneratedMusicOptions
        {
            Generator = generator.Name,
            InstrumentLibrary = library,
            Preset = preset,
        });

        //Act
        provider.Start(SampleRate, 2);
        PullUntilAudible(provider, ModelTimeout);

        //Assert
        provider.State.Should().Be(StreamingMusicState.Playing);
        provider.ActiveSource.Should().NotBeNull();
        provider.ActiveSource!.IsReplay.Should().BeFalse();
        provider.ActiveSource.GeneratorName.Should().Be(generator.Name);
        provider.ActiveSource.InstrumentLibraryName.Should().Be(library);
        provider.ActiveSourceSummary.Should().Contain(generator.Name).And.Contain(library);
    }
}
