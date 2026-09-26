using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.Host.Hosting;

/// <summary>
/// The live game hosts <see cref="GameWindowLifecycle"/> can reach. Hosts add themselves when they are
/// constructed and remove themselves when they are disposed; the list holds them weakly, so a host that is
/// never disposed does not stay alive because of it. Thread-safe.
/// </summary>
internal static class WindowLifecycleParticipants
{
    private static readonly List<WeakReference<IWindowLifecycleParticipant>> Participants = new();

    /// <summary>Adds <paramref name="participant"/> (once).</summary>
    /// <param name="participant">The host to add.</param>
    internal static void Add(IWindowLifecycleParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        lock (Participants)
        {
            Prune(null);
            if (IndexOf(participant) < 0)
                Participants.Add(new WeakReference<IWindowLifecycleParticipant>(participant));
        }
    }

    /// <summary>Removes <paramref name="participant"/>; does nothing if it was not added.</summary>
    /// <param name="participant">The host to remove.</param>
    internal static void Remove(IWindowLifecycleParticipant participant)
    {
        lock (Participants)
        {
            Prune(participant);
        }
    }

    /// <summary>Returns the live hosts, in the order they were added.</summary>
    /// <returns>A snapshot of the live hosts.</returns>
    internal static IReadOnlyList<IWindowLifecycleParticipant> Snapshot()
    {
        lock (Participants)
        {
            var live = new List<IWindowLifecycleParticipant>(Participants.Count);
            foreach (var reference in Participants)
            {
                if (reference.TryGetTarget(out var participant))
                    live.Add(participant);
            }
            return live;
        }
    }

    private static int IndexOf(IWindowLifecycleParticipant participant)
    {
        for (int i = 0; i < Participants.Count; i++)
        {
            if (Participants[i].TryGetTarget(out var candidate) && ReferenceEquals(candidate, participant))
                return i;
        }
        return -1;
    }

    // Drops collected entries, and also 'remove' when it is given.
    private static void Prune(IWindowLifecycleParticipant? remove)
    {
        Participants.RemoveAll(reference =>
            !reference.TryGetTarget(out var candidate) || ReferenceEquals(candidate, remove));
    }
}
