using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Generation;
using CodeBrix.Audio.MusicGeneration.Presets;
using CodeBrix.Audio.MusicGeneration.Replay;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic;

/// <summary>
/// Turns <see cref="GeneratedMusicOptions"/> into what CodeBrix.Audio.MusicGeneration reads: the
/// generator to use, the session options and the request. Everything here is pure mapping over the
/// process-wide registries, so it is tested directly.
/// </summary>
internal static class GeneratedMusicRequests
{
    private static readonly char[] WordSeparators = [' ', '\t', '\r', '\n', ',', ';'];

    /// <summary>Every preset of every family this package knows, in family order.</summary>
    internal static IReadOnlyList<MusicPreset> AllPresets { get; } =
        SkyTNTPresets.All.Concat(MuPTPresets.All).ToArray();

    /// <summary>The family word the SkyTNT presets (and the SkyTNT generator) report.</summary>
    internal static string SkyTNTFamily { get; } = SkyTNTPresets.All[0].Family;

    /// <summary>The family word the MuPT presets (and the MuPT generator) report.</summary>
    internal static string MuPTFamily { get; } = MuPTPresets.All[0].Family;

    /// <summary>
    /// Throws when a preset name belongs to no family at all - a spelling mistake, caught where it
    /// was made rather than when the music starts.
    /// </summary>
    /// <param name="presetName">The preset name, or null for none.</param>
    /// <exception cref="ArgumentException">No family has a preset of that name.</exception>
    internal static void EnsurePresetExists(string? presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName)) { return; }

        if (AllPresets.Any(preset => NameIs(preset.Name, presetName))) { return; }

        throw new ArgumentException(
            $"There is no music preset named '{presetName.Trim()}'. The presets are: {DescribePresets(AllPresets)}.",
            nameof(presetName));
    }

    /// <summary>
    /// Resolves the generator a set of options plays: the named one, else the first registered
    /// generator that is not a built-in replay, else the embedded replay.
    /// </summary>
    /// <param name="generatorName">The name the options give, or null.</param>
    /// <param name="fellBackToReplay">
    /// True when nothing was named and nothing but the built-in replays is registered.
    /// </param>
    /// <returns>The generator.</returns>
    /// <exception cref="InvalidOperationException">A named generator is not registered.</exception>
    internal static IMusicGenerator ResolveGenerator(string? generatorName, out bool fellBackToReplay)
    {
        fellBackToReplay = false;

        if (!string.IsNullOrWhiteSpace(generatorName))
        {
            return MusicGeneratorRegistry.Resolve(generatorName.Trim());
        }

        foreach (IMusicGenerator registered in MusicGeneratorRegistry.Registered)
        {
            if (!EmbeddedReplay.IsReservedName(registered.Name)) { return registered; }
        }

        fellBackToReplay = true;
        return MusicGeneratorRegistry.Resolve(null);
    }

    /// <summary>
    /// Finds the preset of a name written for a generator family.
    /// </summary>
    /// <param name="presetName">The preset name.</param>
    /// <param name="generator">The generator the preset is for.</param>
    /// <returns>The preset.</returns>
    /// <exception cref="ArgumentException">
    /// No preset of that name exists, or it was written for another family.
    /// </exception>
    internal static MusicPreset FindPreset(string presetName, IMusicGenerator generator)
    {
        EnsurePresetExists(presetName);

        MusicPreset? preset = AllPresets.FirstOrDefault(candidate =>
            NameIs(candidate.Name, presetName) && NameIs(candidate.Family, generator.Family));

        if (preset is not null) { return preset; }

        MusicPreset other = AllPresets.First(candidate => NameIs(candidate.Name, presetName));
        MusicPreset[] ownFamily = AllPresets.Where(candidate => NameIs(candidate.Family, generator.Family)).ToArray();

        throw new ArgumentException(
            $"The music preset '{other.Name}' is written for the {other.Family} family, but the generator " +
            $"'{generator.Name}' is {generator.Family}. " +
            (ownFamily.Length == 0
                ? $"There are no presets for the {generator.Family} family; register and name a generator of " +
                  $"the {other.Family} family to use this preset."
                : $"Its presets are: {DescribePresets(ownFamily)}."),
            nameof(presetName));
    }

    /// <summary>
    /// Builds the request for a preset or for words, with an optional seed and tempo.
    /// </summary>
    /// <param name="generator">The generator the request goes to.</param>
    /// <param name="presetName">A preset name, or null.</param>
    /// <param name="text">Words, or null; they win over the preset.</param>
    /// <param name="seed">A seed, or null.</param>
    /// <param name="intentBeatsPerMinute">A tempo to ASK THE GENERATOR for, or null.</param>
    /// <param name="suggestedRendition">The preset's suggested voicing, or null.</param>
    /// <returns>A new request.</returns>
    internal static MusicRequest BuildRequest(
        IMusicGenerator generator,
        string? presetName,
        string? text,
        int? seed,
        double? intentBeatsPerMinute,
        out string? suggestedRendition)
    {
        suggestedRendition = null;
        MusicRequest request;

        if (!string.IsNullOrWhiteSpace(text))
        {
            request = FromWords(text);
        }
        else if (!string.IsNullOrWhiteSpace(presetName))
        {
            MusicPreset preset = FindPreset(presetName, generator);
            request = preset.CreateRequest();
            suggestedRendition = preset.SuggestedRendition;
        }
        else
        {
            request = new MusicRequest();
        }

        if (seed.HasValue) { request.Seed = seed.Value; }

        if (intentBeatsPerMinute.HasValue)
        {
            request.Intent ??= new MusicIntent();
            request.Intent.BeatsPerMinute = intentBeatsPerMinute.Value;
        }

        return request;
    }

    /// <summary>
    /// Words become CHARACTER WORDS when every one of them is a known one, and free text otherwise.
    /// </summary>
    /// <param name="text">The words.</param>
    /// <returns>A new request carrying them.</returns>
    internal static MusicRequest FromWords(string text)
    {
        string[] words = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        var request = new MusicRequest();

        if (words.Length > 0 && words.All(MusicCharacterWords.IsKnown))
        {
            request.Intent ??= new MusicIntent();
            foreach (string word in words) { request.Intent.CharacterWords.Add(word); }
        }
        else
        {
            request.Text = text.Trim();
        }

        return request;
    }

    /// <summary>
    /// The segment priming listening settled on for a family: SkyTNT takes turns, MuPT starts every
    /// segment fresh, anything else keeps the music package's default.
    /// </summary>
    /// <param name="family">The generator family.</param>
    /// <returns>The priming, or null for the package default.</returns>
    internal static SegmentPriming? DefaultPrimingFor(string family)
    {
        if (NameIs(family, SkyTNTFamily)) { return SegmentPriming.Alternate; }
        if (NameIs(family, MuPTFamily)) { return SegmentPriming.Fresh; }
        return null;
    }

    /// <summary>
    /// Maps options onto the session options for one start.
    /// </summary>
    /// <param name="options">The game's options.</param>
    /// <param name="sampleRate">The engine output's sample rate.</param>
    /// <param name="generator">The generator the session plays.</param>
    /// <param name="fellBackToReplay">True when the embedded replay plays because no model is registered.</param>
    /// <returns>The session options: the application owns the output, at the engine's rate.</returns>
    /// <exception cref="InvalidOperationException">A named generator is not registered.</exception>
    /// <exception cref="ArgumentException">The preset does not fit the generator.</exception>
    internal static MusicGenerationOptions ToSessionOptions(
        GeneratedMusicOptions options,
        int sampleRate,
        out IMusicGenerator generator,
        out bool fellBackToReplay)
    {
        generator = ResolveGenerator(options.Generator, out fellBackToReplay);

        var session = new MusicGenerationOptions
        {
            ApplicationOwnsAudioOutput = true,
            SampleRate = sampleRate,
            Generator = generator.Name,
            InstrumentLibrary = string.IsNullOrWhiteSpace(options.InstrumentLibrary) ? null : options.InstrumentLibrary.Trim(),
            MasterVolume = options.MasterVolume,
            SeamCrossfade = options.SeamCrossfade < TimeSpan.Zero ? TimeSpan.Zero : options.SeamCrossfade,
        };

        SegmentPriming? priming = options.SegmentPriming ?? DefaultPrimingFor(generator.Family);
        if (priming.HasValue) { session.SegmentPriming = priming.Value; }

        if (options.BeatsPerMinute.HasValue)
        {
            session.SessionBeatsPerMinute = options.BeatsPerMinute.Value;
            session.TempoPolicy = SessionTempoPolicy.Carry;
        }

        if (fellBackToReplay)
        {
            //The embedded replay honours nothing but a continuation, so the parts of the request meant
            //  for a model are left out rather than refused: this is the expected degraded path
            session.Request = new MusicRequest();
        }
        else
        {
            session.Request = BuildRequest(generator, options.Preset, options.Text, options.Seed, null, out string? rendition);
            if (rendition is not null) { session.Rendition = rendition; }
        }

        return session;
    }

    /// <summary>What the request parts left out on the replay path were, for the log line.</summary>
    /// <param name="options">The game's options.</param>
    /// <returns>A clause such as " (the preset 'ClubArrangement' is not used)", or an empty string.</returns>
    internal static string DescribeUnusedRequestParts(GeneratedMusicOptions options)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.Text)) { parts.Add($"the words '{options.Text.Trim()}'"); }
        else if (!string.IsNullOrWhiteSpace(options.Preset)) { parts.Add($"the preset '{options.Preset.Trim()}'"); }

        if (options.Seed.HasValue) { parts.Add("the seed"); }

        return parts.Count == 0 ? string.Empty : $"; {string.Join(" and ", parts)} cannot be used by a replay";
    }

    private static bool NameIs(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string DescribePresets(IEnumerable<MusicPreset> presets) =>
        string.Join(", ", presets.Select(preset => $"{preset.Name} ({preset.Family})"));
}
