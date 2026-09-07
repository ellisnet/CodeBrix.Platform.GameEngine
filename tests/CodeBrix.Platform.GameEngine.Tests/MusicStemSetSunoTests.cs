using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Platform.GameEngine.Audio;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers <see cref="MusicStemSet.FromSunoStems(string, string, string[])"/>: which stems it takes,
/// what it refuses, where the grid comes from, and that a zip and a folder holding the same files
/// produce the same set.
/// </summary>
/// <remarks>
/// <para>
/// The export is BUILT BY THE TEST — three same-length 48 kHz stereo WAVs named the way a stems
/// download names them, and a MIDI file shaped the way a generated one is: a tempo event on every
/// beat, a key signature the specification does not allow, and no time signature. Nothing binary is
/// committed, and no real download is needed to run the suite.
/// </para>
/// <para>
/// A stem is a constant sample value rather than a tone, so a test can read the mix and say WHICH
/// layer it is hearing.
/// </para>
/// <para>
/// Nothing here opens an audio device — a set builds its output voice on first play, and these
/// never play. The audio system is shut down after each test anyway, following
/// <see cref="MidiMusicTrackLayerTests"/>, so no sample rate this suite touched outlives it.
/// </para>
/// </remarks>
public class MusicStemSetSunoTests : IDisposable
{
    private const int SampleRate = 48000;
    private const double StemSeconds = 0.25;

    private const double VocalsValue = 0.25;
    private const double DrumsValue = 0.5;
    private const double BassValue = 0.125;

    private const double FirstBeatsPerMinute = 100;
    private const double SecondBeatsPerMinute = 120;

    private readonly MusicManager _manager = MusicManager.Instance;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "codebrix-gameengine-stems-" + Guid.NewGuid().ToString("N"));

    /// <summary>Takes manual control of the fade clock and clears the mixer.</summary>
    public MusicStemSetSunoTests()
    {
        Directory.CreateDirectory(_directory);

        _manager.Ticker.CancelAll();
        _manager.Ticker.ManualTickingForTests = true;
        AudioMixer.Reset();
    }

    /// <summary>Removes the generated export and leaves the audio system as it was found.</summary>
    public void Dispose()
    {
        _manager.Ticker.CancelAll();
        AudioMixer.Reset();
        AudioSystem.Shutdown();

        try
        {
            Directory.Delete(_directory, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    // ----- which stems come back -----

    [Fact]
    public void Naming_no_stems_loads_every_stem_that_carries_audio()
    {
        //Arrange
        var folder = BuildStemsFolder();

        //Act
        using var set = MusicStemSet.FromSunoStems("song", folder);

        //Assert - in the export's own order, which is the reader's vocabulary order and not the
        //order the files happened to be listed in.
        set.Count.Should().Be(3);
        set.Stems.Select(stem => stem.Name).Should().Equal("Vocals", "Drums", "Bass");
    }

    [Fact]
    public void The_stems_asked_for_come_back_in_the_order_they_were_asked_for()
    {
        //Arrange
        var folder = BuildStemsFolder();

        //Act - and the names come off someone else's file names, so the match ignores case.
        using var set = MusicStemSet.FromSunoStems("song", folder, "bass", "VOCALS");

        //Assert
        set.Count.Should().Be(2);
        set.Stems.Select(stem => stem.Name).Should().Equal("Bass", "Vocals");
        set["bass"].Should().BeSameAs(set[0]);
    }

    [Fact]
    public void A_name_that_is_not_in_the_export_is_refused_with_the_names_that_are()
    {
        //Arrange
        var folder = BuildStemsFolder();

        //Act
        var act = () => MusicStemSet.FromSunoStems("song", folder, "Drums", "Theremin");

        //Assert - "it did not work" against "you meant Drums" is the whole difference here.
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Theremin*Vocals*Drums*Bass*");
    }

    [Fact]
    public void An_export_that_carries_no_audio_is_refused()
    {
        //Arrange - a MIDI-only export: there is nothing to layer, and the set constructor's own
        //complaint would not say why.
        var folder = Path.Combine(_directory, "Silent Song Stems");
        Directory.CreateDirectory(folder);
        SyntheticInstrumentAssets.WriteMidiWithAPerBeatTempoMap(
            Path.Combine(folder, "Silent Song (Drums).mid"), FirstBeatsPerMinute, SecondBeatsPerMinute);

        //Act
        var act = () => MusicStemSet.FromSunoStems("song", folder);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*no audio stems*");
    }

    [Fact]
    public void A_missing_export_is_reported_as_a_missing_file()
    {
        //Arrange & Act
        var act = () => MusicStemSet.FromSunoStems("song", Path.Combine(_directory, "Nothing Stems.zip"));

        //Assert
        act.Should().Throw<FileNotFoundException>();
    }

    // ----- the grid -----

    [Fact]
    public void The_grid_comes_from_the_midi_that_ships_with_the_stems()
    {
        //Arrange
        var folder = BuildStemsFolder();

        //Act
        using var set = MusicStemSet.FromSunoStems("song", folder);

        //Assert - the tempo the export starts at, four beats to the bar (it carries no time
        //signature), and the whole map behind it.
        set.Timeline.Should().NotBeNull();
        set.Timeline!.BeatsPerMinute.Should().BeApproximately(FirstBeatsPerMinute, 0.001);
        set.Timeline.BeatsPerBar.Should().Be(4);
        set.Timeline.HasTempoChanges.Should().BeTrue();
        set.Timeline.TempoMap.Should().NotBeNull();
    }

    [Fact]
    public void The_grid_follows_the_tempo_through_the_change()
    {
        //Arrange - the change lands on beat 4, so a position of three seconds is inside the second
        //bar and quantising against the FIRST tempo alone would answer late.
        var folder = BuildStemsFolder();
        using var set = MusicStemSet.FromSunoStems("song", folder);

        var map = set.Timeline!.TempoMap!;
        var position = TimeSpan.FromSeconds(3);

        //Act
        var wait = set.Timeline.TimeToNextBoundary(position, MusicTransitionQuantize.Bar);

        //Assert - the map's own arithmetic, not a constant.
        var expected = map.TimeAt(8) - position;
        wait.TotalSeconds.Should().BeApproximately(expected.TotalSeconds, 0.001);

        var constantGrid = (60.0 / FirstBeatsPerMinute * 4) - (3.0 % (60.0 / FirstBeatsPerMinute * 4));
        Math.Abs(wait.TotalSeconds - constantGrid).Should().BeGreaterThan(0.3);
    }

    [Fact]
    public void An_export_with_no_midi_offers_no_grid()
    {
        //Arrange - the tempo of a recording cannot be inferred, here or anywhere else in the engine.
        var folder = BuildStemsFolder(withMidi: false);

        //Act
        using var set = MusicStemSet.FromSunoStems("song", folder);

        //Assert
        set.Timeline.Should().BeNull();
    }

    // ----- the audio -----

    [Fact]
    public void Only_the_first_stem_asked_for_is_audible()
    {
        //Arrange - a set that came up with every layer at full would be the loudest possible first
        //impression of the feature, so the rest are brought in deliberately.
        var folder = BuildStemsFolder();
        using var set = MusicStemSet.FromSunoStems("song", folder, "Drums", "Vocals", "Bass");

        //Act - the first block ramps the gains up from silence, so settle it and read the second.
        var provider = ProviderOf(set);
        Read(provider, frames: 64);
        var block = Read(provider, frames: 64);

        //Assert - the mix is exactly the Drums layer, which is the one that was asked for first.
        set["Drums"].Gain.Should().Be(1f);
        set["Vocals"].Gain.Should().Be(0f);
        set["Bass"].Gain.Should().Be(0f);

        block[0].Should().BeApproximately((float)DrumsValue, 0.001f);
        block[127].Should().BeApproximately((float)DrumsValue, 0.001f);
    }

    [Fact]
    public void The_stems_line_up_because_the_export_says_they_do()
    {
        //Arrange
        var folder = BuildStemsFolder();
        using var set = MusicStemSet.FromSunoStems("song", folder);

        //Act - every layer in, so the mix is the sum of all three.
        var provider = ProviderOf(set);
        set["Vocals"].Gain = 1f;
        set["Drums"].Gain = 1f;
        set["Bass"].Gain = 1f;
        Read(provider, frames: 64);
        var block = Read(provider, frames: 64);

        //Assert - one sample rate, one channel count and one length, which is what a stems export
        //guarantees and what a stem set requires.
        set.Duration.Should().Be(TimeSpan.FromSeconds(StemSeconds));
        block[0].Should().BeApproximately((float)(VocalsValue + DrumsValue + BassValue), 0.001f);
    }

    // ----- what the export said about itself -----

    [Fact]
    public void A_clean_export_reports_no_problems()
    {
        //Arrange
        var folder = BuildStemsFolder();

        //Act
        using var set = MusicStemSet.FromSunoStems("song", folder);

        //Assert
        set.Problems.Should().BeEmpty();
    }

    [Fact]
    public void What_the_export_could_not_account_for_is_reported_on_the_set()
    {
        //Arrange - a stem name outside the vocabulary the reader knows. It loads and plays; the
        //point of reporting it is that no instrument defaults were assumed for it.
        var folder = BuildStemsFolder(withUnknownStem: true);

        //Act
        using var set = MusicStemSet.FromSunoStems("song", folder);

        //Assert
        set.Count.Should().Be(4);
        set.Problems.Should().NotBeEmpty();
        set.Problems.Any(problem => problem.Contains("Theremin", StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public void A_set_built_the_ordinary_way_reports_no_problems_at_all()
    {
        //Arrange & Act - the property belongs to the stems-export route and must not read as an
        //empty promise on a set that never had an export behind it.
        using var set = new MusicStemSet("plain", new[] { "one" }, new[] { Silence() });

        //Assert
        set.Problems.Should().BeEmpty();
    }

    // ----- the zip form -----

    [Fact]
    public void A_zip_of_the_same_files_loads_identically()
    {
        //Arrange
        var folder = BuildStemsFolder();
        var zipPath = Path.Combine(_directory, "Fake Song Stems.zip");
        ZipFile.CreateFromDirectory(folder, zipPath);

        var options = new SunoLoadOptions { CacheFolder = Path.Combine(_directory, "cache-identical") };

        //Act
        using var fromFolder = MusicStemSet.FromSunoStems("folder", folder);
        using var fromZip = MusicStemSet.FromSunoStems("zip", zipPath, options);

        //Assert - same stems, same order, same grid. A zip is extracted on demand into the cache
        //folder; a folder is read where it lies.
        fromZip.Stems.Select(stem => stem.Name).Should().Equal(fromFolder.Stems.Select(stem => stem.Name));
        fromZip.Timeline.Should().NotBeNull();
        fromZip.Timeline!.BeatsPerMinute.Should().BeApproximately(fromFolder.Timeline!.BeatsPerMinute, 0.001);
        fromZip.Timeline.HasTempoChanges.Should().BeTrue();
        fromZip.Duration.Should().Be(fromFolder.Duration);
    }

    [Fact]
    public void A_zip_extracts_only_the_stems_that_were_asked_for()
    {
        //Arrange - alignment measurement would decode the Drums stem (it is the one with MIDI), and
        //decoding it would extract it. Asking for the Vocals alone must leave the Drums packed,
        //which is the observable half of "alignment is a MIDI concern and is turned off here".
        var folder = BuildStemsFolder();
        var zipPath = Path.Combine(_directory, "Fake Song Stems.zip");
        ZipFile.CreateFromDirectory(folder, zipPath);

        var cacheFolder = Path.Combine(_directory, "cache-selective");
        var options = new SunoLoadOptions { CacheFolder = cacheFolder, MeasureAlignment = true };

        //Act
        using var set = MusicStemSet.FromSunoStems("song", zipPath, options, "Vocals");

        //Assert
        var extracted = Directory.GetFiles(cacheFolder).Select(Path.GetFileName).ToArray();
        extracted.Should().Contain("Fake Song (Vocals).wav");
        extracted.Should().NotContain("Fake Song (Drums).wav");

        //And the caller's own options object is untouched: they were copied before being changed.
        options.MeasureAlignment.Should().BeTrue();
        options.CacheFolder.Should().Be(cacheFolder);
    }

    // ----- helpers -----

    // Writes an export the way a stems download is laid out: "<Title> (<Stem>).<ext>" files side by
    // side, all the same length, rate and channel count, with the MIDI beside the recordings.
    private string BuildStemsFolder(bool withMidi = true, bool withUnknownStem = false)
    {
        var folder = Path.Combine(_directory, "Fake Song Stems");
        Directory.CreateDirectory(folder);

        Wav(folder, "Vocals", VocalsValue);
        Wav(folder, "Drums", DrumsValue);
        Wav(folder, "Bass", BassValue);

        if (withUnknownStem)
        {
            Wav(folder, "Theremin", 0.0625);
        }

        if (withMidi)
        {
            SyntheticInstrumentAssets.WriteMidiWithAPerBeatTempoMap(
                Path.Combine(folder, "Fake Song (Drums).mid"), FirstBeatsPerMinute, SecondBeatsPerMinute);
        }

        return folder;
    }

    private static void Wav(string folder, string stemName, double value) =>
        SyntheticInstrumentAssets.WriteStereoWav(
            Path.Combine(folder, $"Fake Song ({stemName}).wav"), StemSeconds, value, SampleRate);

    private CachedSound Silence() =>
        CachedSound.FromFile(SyntheticInstrumentAssets.WriteStereoWav(
            Path.Combine(_directory, "plain.wav"), seconds: 0.01, value: 0, SampleRate));

    private static StemMixSampleProvider ProviderOf(MusicStemSet set)
        => (StemMixSampleProvider)typeof(MusicStemSet)
            .GetField("_provider", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(set)!;

    private static float[] Read(StemMixSampleProvider provider, int frames)
    {
        var buffer = new float[frames * 2];
        provider.Read(buffer);
        return buffer;
    }
}
