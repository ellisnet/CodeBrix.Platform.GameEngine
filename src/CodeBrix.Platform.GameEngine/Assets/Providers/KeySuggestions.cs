using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.Assets.Providers; //CodeBrix (not from Gondwana)

/// <summary>
/// Finds the few real keys or names closest to one that was not found, so a "not found" message can
/// say "Did you mean: ...". Only ever called on a failure path.
/// </summary>
/// <remarks>
/// Closeness is a case-insensitive edit distance over the whole string, or over its last segment
/// (after the last <c>/</c> or <c>:</c>) plus a small penalty, whichever is smaller, so a misspelled
/// file name and a right name in the wrong folder or pack are both found. A candidate further away
/// than about a third of the wanted string's length is never suggested.
/// </remarks>
internal static class KeySuggestions
{
    /// <summary>The most suggestions a message carries.</summary>
    internal const int MaxSuggestions = 3;

    //What a match on the last segment alone costs over a match on the whole string
    private const int SegmentPenalty = 2;

    /// <summary>
    /// Gets the candidates closest to <paramref name="wanted"/>, best first.
    /// </summary>
    /// <param name="wanted">The key or name that was not found.</param>
    /// <param name="candidates">The real keys or names.</param>
    /// <param name="max">The most suggestions to return.</param>
    /// <returns>At most <paramref name="max"/> candidates, closest first; empty when none is close.</returns>
    internal static IReadOnlyList<string> Closest(string? wanted, IEnumerable<string?> candidates, int max = MaxSuggestions)
    {
        if (string.IsNullOrWhiteSpace(wanted) || candidates is null || max <= 0) { return []; }

        string wantedLower = wanted.ToLowerInvariant();
        string wantedSegment = LastSegment(wantedLower);
        int fullLimit = Math.Max(2, wantedLower.Length / 3);
        int segmentLimit = Math.Max(1, wantedSegment.Length / 3);
        List<(string Candidate, int Score)> scored = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate) || !seen.Add(candidate)) { continue; }

            string candidateLower = candidate.ToLowerInvariant();
            int score = BoundedDistance(wantedLower, candidateLower, fullLimit);

            int segmentDistance = BoundedDistance(wantedSegment, LastSegment(candidateLower), segmentLimit);
            if (segmentDistance <= segmentLimit)
            {
                score = Math.Min(score, segmentDistance + SegmentPenalty);
            }

            if (score <= fullLimit) { scored.Add((candidate, score)); }
        }

        return scored
            .OrderBy(pair => pair.Score)
            .ThenBy(pair => pair.Candidate, StringComparer.Ordinal)
            .Take(max)
            .Select(pair => pair.Candidate)
            .ToList();
    }

    /// <summary>
    /// Builds the " Did you mean: 'a', 'b'?" sentence to append to a not-found message.
    /// </summary>
    /// <param name="wanted">The key or name that was not found.</param>
    /// <param name="candidates">
    /// Produces the real keys or names. It is only called here, and anything it throws is swallowed,
    /// because a suggestion must never replace the error it decorates.
    /// </param>
    /// <returns>The sentence with a leading space, or an empty string when nothing is close.</returns>
    internal static string DidYouMean(string? wanted, Func<IEnumerable<string?>> candidates)
    {
        IReadOnlyList<string> closest;

        try
        {
            closest = Closest(wanted, candidates());
        }
        catch (Exception)
        {
            //Diagnostics only: a provider that cannot list its keys leaves the message as it was
            return string.Empty;
        }

        return closest.Count == 0
            ? string.Empty
            : $" Did you mean: {string.Join(", ", closest.Select(candidate => $"'{candidate}'"))}?";
    }

    private static string LastSegment(string value)
    {
        int index = value.LastIndexOfAny(['/', ':']);

        return index < 0 ? value : value[(index + 1)..];
    }

    //Levenshtein distance, giving up (and returning limit + 1) once it must exceed the limit
    private static int BoundedDistance(string left, string right, int limit)
    {
        if (Math.Abs(left.Length - right.Length) > limit) { return limit + 1; }

        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];

        for (int column = 0; column <= right.Length; column++) { previous[column] = column; }

        for (int row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            int rowMinimum = row;

            for (int column = 1; column <= right.Length; column++)
            {
                int cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > limit) { return limit + 1; }

            (previous, current) = (current, previous);
        }

        return Math.Min(previous[right.Length], limit + 1);
    }
}
