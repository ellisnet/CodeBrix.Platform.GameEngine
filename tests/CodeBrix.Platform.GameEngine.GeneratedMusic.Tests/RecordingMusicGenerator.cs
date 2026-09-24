using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MusicGeneration;
using CodeBrix.Audio.MusicGeneration.Generation;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// A generator that claims a family of the test's choosing and honours every request feature, but
/// writes the embedded replay's music - so a test can exercise the preset and follow-up paths of a
/// model family without loading a model. It records every request it is given.
/// </summary>
internal sealed class RecordingMusicGenerator : IMusicGenerator
{
    private readonly object _gate = new();
    private readonly List<MusicRequest> _requests = [];
    private readonly IMusicGenerator _music;

    internal RecordingMusicGenerator(string name, string family)
    {
        Name = name;
        Family = family;
        _music = MusicGeneratorRegistry.Resolve(null);
    }

    public string Name { get; }

    public string Family { get; }

    public string Description => $"A test generator of the {Family} family, playing the embedded replay.";

    public MusicRequestFeatures Honours { get; } =
        Enum.GetValues<MusicRequestFeatures>().Aggregate(MusicRequestFeatures.None, (all, one) => all | one);

    public bool IsLoaded { get; private set; }

    internal int ReleaseCount { get; private set; }

    /// <summary>A snapshot of the requests given so far, oldest first.</summary>
    internal IReadOnlyList<MusicRequest> Requests
    {
        get { lock (_gate) { return _requests.ToArray(); } }
    }

    public Task PreloadAsync(CancellationToken cancellationToken)
    {
        IsLoaded = true;
        return Task.CompletedTask;
    }

    public void Release()
    {
        IsLoaded = false;
        ReleaseCount++;
    }

    public async IAsyncEnumerable<GeneratedMusicEvent> GenerateAsync(
        MusicRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lock (_gate) { _requests.Add(request.Clone()); }

        IsLoaded = true;

        //The replay honours a continuation and nothing else, so it is handed only the plumbing
        var plain = new MusicRequest
        {
            TicksPerQuarterNote = request.TicksPerQuarterNote,
            PaceInRealTime = request.PaceInRealTime,
            Continuation = request.Continuation,
        };

        await foreach (GeneratedMusicEvent generated in _music.GenerateAsync(plain, cancellationToken))
        {
            yield return generated;
        }
    }
}
