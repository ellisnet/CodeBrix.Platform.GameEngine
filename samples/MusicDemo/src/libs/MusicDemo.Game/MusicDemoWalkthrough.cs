using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Audio;
using Microsoft.Extensions.Logging;

namespace MusicDemo.Game;

/// <summary>
/// An unattended pass over the parts of the demo that cannot be checked by looking at it: the two
/// instrument formats, what each file said about its own tempo, and whether a bar-quantised
/// transition across a tempo change lands where the tempo map says it should.
/// </summary>
/// <remarks>
/// <para>
/// MAINTAINER TOOL, NOT A FEATURE OF THE SAMPLE. It runs only when the environment variable
/// <c>MUSICDEMO_SELFTEST</c> is set to <c>1</c>, so a person running the demo never sees it. It
/// exists because these are audible-only behaviours: on a machine with no sound card, and in a
/// terminal, the log is the only evidence that the music system did what it says.
/// </para>
/// <para>
/// It drives the same methods the buttons drive, from a worker thread — every
/// <see cref="MusicManager"/> method is safe to call from any thread — and writes one line per
/// check, then a RESULT line. Nothing here is used by the demo's own controls.
/// </para>
/// </remarks>
internal static class MusicDemoWalkthrough
{
    private const string Prefix = "MUSICDEMO-SELFTEST";

    /// <summary>Whether the environment asked for the walkthrough.</summary>
    internal static bool IsRequested =>
        string.Equals(Environment.GetEnvironmentVariable("MUSICDEMO_SELFTEST"), "1", StringComparison.Ordinal);

    /// <summary>Starts the walkthrough on a worker thread and returns immediately.</summary>
    /// <param name="demo">The running demo.</param>
    internal static void Start(MusicDemoGame demo) => Task.Run(() => Run(demo));

    private static void Run(MusicDemoGame demo)
    {
        var passed = 0;
        var total = 0;

        try
        {
            // The engine has just started and the tracks have just been built; let the first frames
            // and the first audio blocks go by before asking anything about them.
            Thread.Sleep(2000);

            var sampler = demo.SamplerTrack;
            var stems = demo.SongStems;

            Check(ref passed, ref total, "ds-track-loaded",
                sampler?.Timeline is not null && sampler.Problems.Count == 0,
                $"instrument problems {sampler?.Problems.Count}, timeline {(sampler?.Timeline is null ? "none" : "present")}");

            Check(ref passed, ref total, "ds-track-grid",
                sampler?.Timeline is not null
                && Math.Abs(sampler.Timeline.BeatsPerMinute - MusicAssetFactory.BeatsPerMinute) < 0.5
                && sampler.Timeline.HasTempoChanges,
                $"{sampler?.Timeline?.BeatsPerMinute:0} BPM to start, tempo changes {sampler?.Timeline?.HasTempoChanges}");

            Check(ref passed, ref total, "stems-loaded",
                stems is not null && stems.Count == 3 && stems.Problems.Count == 0
                && stems.Stems.Select(stem => stem.Name).SequenceEqual(new[] { "Vocals", "Drums", "Bass" }),
                $"{stems?.Count} stems ({string.Join(", ", stems?.Stems.Select(stem => stem.Name) ?? Array.Empty<string>())}), "
                + $"export problems {stems?.Problems.Count}");

            Check(ref passed, ref total, "stems-grid",
                stems?.Timeline is not null
                && Math.Abs(stems.Timeline.BeatsPerMinute - MusicAssetFactory.StemsBeatsPerMinute) < 0.5
                && stems.Timeline.HasTempoChanges,
                $"{stems?.Timeline?.BeatsPerMinute:0} BPM to start, tempo changes {stems?.Timeline?.HasTempoChanges}");

            // ----- a bar-quantised transition ACROSS the tempo change -----

            demo.PlayDecentSamplerTheme();
            Thread.Sleep(5500);

            var tempoChangeAt = MusicAssetFactory.BarsBeforeTempoChange * MusicAssetFactory.BeatsPerBar
                                * 60.0 / MusicAssetFactory.BeatsPerMinute;
            var position = MusicManager.Instance.NowPlaying?.Position ?? TimeSpan.Zero;

            Check(ref passed, ref total, "ds-plays-past-the-tempo-change",
                string.Equals(MusicManager.Instance.NowPlaying?.Key, sampler?.Key, StringComparison.Ordinal)
                && position.TotalSeconds > tempoChangeAt,
                $"'{MusicManager.Instance.NowPlaying?.Key}' at {position.TotalSeconds:0.000}s, "
                + $"the tempo changed at {tempoChangeAt:0.000}s");

            var timeline = sampler?.Timeline;
            var wait = timeline?.TimeToNextBoundary(position, MusicTransitionQuantize.Bar) ?? TimeSpan.Zero;
            var atOpeningTempo = timeline is null
                ? 0
                : timeline.SecondsPerBar - (position.TotalSeconds % timeline.SecondsPerBar);

            Check(ref passed, ref total, "grid-follows-the-tempo-map",
                timeline is not null && Math.Abs(wait.TotalSeconds - atOpeningTempo) > 0.1,
                $"the map says {wait.TotalSeconds:0.000}s to the next bar; a grid fixed at the opening "
                + $"tempo would have said {atOpeningTempo:0.000}s");

            demo.CrossfadeToTrackA(MusicTransitionQuantize.Bar);

            Check(ref passed, ref total, "transition-queued",
                MusicManager.Instance.HasPendingTransition,
                $"waiting {wait.TotalSeconds:0.000}s for the next bar");

            Thread.Sleep((int)(wait.TotalMilliseconds + 1500));

            Check(ref passed, ref total, "transition-landed",
                !MusicManager.Instance.HasPendingTransition
                && string.Equals(MusicManager.Instance.NowPlaying?.Key, "Track A", StringComparison.Ordinal),
                $"now playing '{MusicManager.Instance.NowPlaying?.Key}'");

            // ----- the stems export -----

            demo.PlaySongStems();
            Thread.Sleep(1500);

            Check(ref passed, ref total, "stems-play",
                stems is not null && stems.IsPlaying && stems.Position > TimeSpan.Zero
                && stems[0].Gain > 0.99f && stems[1].Gain < 0.01f,
                $"'{MusicManager.Instance.NowPlaying?.Key}' at {stems?.Position.TotalSeconds:0.000}s, "
                + $"gains {string.Join("/", stems?.Stems.Select(stem => stem.Gain.ToString("0.00")) ?? Array.Empty<string>())}");

            demo.FadeSongStem("Drums", 1f);
            Thread.Sleep(2600);

            Check(ref passed, ref total, "stems-layer-fades-in",
                stems is not null && stems["Drums"].Gain > 0.9f,
                $"Drums at {stems?["Drums"].Gain:0.00}");
        }
        catch (Exception exception)
        {
            total++;
            Engine.Logger.LogError(exception, "{Prefix}: the walkthrough threw.", Prefix);
        }

        Engine.Logger.LogInformation("{Prefix}: RESULT {Result} ({Passed}/{Total} checks)",
            Prefix, passed == total && total > 0 ? "PASS" : "FAIL", passed, total);
    }

    private static void Check(ref int passed, ref int total, string name, bool condition, string detail)
    {
        total++;

        if (condition)
        {
            passed++;
        }

        var line = new StringBuilder()
            .Append(condition ? "PASS " : "FAIL ")
            .Append(name)
            .Append(" - ")
            .Append(detail)
            .ToString();

        Engine.Logger.LogInformation("{Prefix}: {Line}", Prefix, line);
    }
}
