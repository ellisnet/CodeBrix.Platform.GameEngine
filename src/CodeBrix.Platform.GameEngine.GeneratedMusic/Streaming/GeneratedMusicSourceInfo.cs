using System;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic;

/// <summary>
/// What generated music is really playing, as plain values: the generator, its family, the
/// instrument library it plays through, and whether it is the embedded replay rather than a model.
/// </summary>
/// <remarks>
/// A snapshot of the music-generation library's active source that a game (or its test fake) can
/// construct - read it from <see cref="IGeneratedMusicSession.ActiveSourceInfo"/> or
/// <see cref="GeneratedMusicProvider.ActiveSourceInfo"/>.
/// </remarks>
public sealed class GeneratedMusicSourceInfo
{
    /// <summary>Creates a source description.</summary>
    /// <param name="generatorName">The generator's name (a model, or the embedded replay).</param>
    /// <param name="generatorFamily">The generator's family (which presets fit it).</param>
    /// <param name="instrumentLibraryName">The instrument library the music plays through.</param>
    /// <param name="isReplay">Whether no model is playing: the embedded replay is.</param>
    /// <exception cref="ArgumentNullException">A name is null.</exception>
    public GeneratedMusicSourceInfo(string generatorName, string generatorFamily, string instrumentLibraryName, bool isReplay)
    {
        GeneratorName = generatorName ?? throw new ArgumentNullException(nameof(generatorName));
        GeneratorFamily = generatorFamily ?? throw new ArgumentNullException(nameof(generatorFamily));
        InstrumentLibraryName = instrumentLibraryName ?? throw new ArgumentNullException(nameof(instrumentLibraryName));
        IsReplay = isReplay;
    }

    /// <summary>The generator's name (a model, or the embedded replay).</summary>
    public string GeneratorName { get; }

    /// <summary>The generator's family (which presets fit it).</summary>
    public string GeneratorFamily { get; }

    /// <summary>The instrument library the music plays through.</summary>
    public string InstrumentLibraryName { get; }

    /// <summary>Whether no model is playing: the embedded replay is.</summary>
    public bool IsReplay { get; }

    /// <summary>The source as one short line: <c>"&lt;generator&gt; (&lt;family&gt;) through &lt;library&gt;"</c>, with <c>", replay"</c> for the replay.</summary>
    /// <returns>The line.</returns>
    public override string ToString() =>
        $"{GeneratorName} ({GeneratorFamily}) through {InstrumentLibraryName}{(IsReplay ? ", replay" : string.Empty)}";
}
