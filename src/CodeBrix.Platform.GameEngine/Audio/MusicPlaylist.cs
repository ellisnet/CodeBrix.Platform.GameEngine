using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.GameEngine.Audio; //CodeBrix (not from Gondwana)

/// <summary>How a <see cref="MusicPlaylist"/> behaves when a track ends.</summary>
public enum MusicRepeatMode
{
    /// <summary>Stop after the last track.</summary>
    None = 0,

    /// <summary>Repeat the current track forever.</summary>
    One = 1,

    /// <summary>Start again from the first track after the last one.</summary>
    All = 2,
}

/// <summary>
/// An ordered set of tracks the <see cref="MusicManager"/> plays in turn, with repeat modes and
/// shuffle.
/// </summary>
/// <remarks>
/// <para>
/// The playlist does not own its tracks and never disposes them — a game that builds tracks for a
/// playlist disposes them when the level unloads.
/// </para>
/// <para>
/// Shuffle is SEEDED, so a run can be reproduced in a test, and it avoids replaying the track that
/// just finished when it reshuffles: the "shuffle played the same song twice in a row" complaint is
/// a real one, and it comes from reshuffling without looking at what was last heard.
/// </para>
/// </remarks>
public sealed class MusicPlaylist
{
    private readonly object _gate = new();
    private readonly List<MusicTrack> _tracks = new();
    private readonly List<int> _order = new();

    private Random _random;
    private int _position = -1;
    private bool _orderIsStale = true;

    /// <summary>Creates an empty playlist.</summary>
    public MusicPlaylist()
        : this(0)
    { }

    /// <summary>Creates an empty playlist with a fixed shuffle seed.</summary>
    /// <param name="shuffleSeed">The seed for shuffling. 0 uses a time-based seed.</param>
    public MusicPlaylist(int shuffleSeed)
    {
        ShuffleSeed = shuffleSeed;
        _random = shuffleSeed == 0 ? new Random() : new Random(shuffleSeed);
    }

    /// <summary>What happens when a track ends. Defaults to <see cref="MusicRepeatMode.All"/>.</summary>
    public MusicRepeatMode RepeatMode { get; set; } = MusicRepeatMode.All;

    /// <summary>The seed used for shuffling; 0 means a time-based seed.</summary>
    public int ShuffleSeed { get; }

    /// <summary>The tracks, in the order they were added.</summary>
    public IReadOnlyList<MusicTrack> Tracks
    {
        get { lock (_gate) { return _tracks.ToArray(); } }
    }

    /// <summary>The track the playlist is on, or <see langword="null"/> before it starts.</summary>
    public MusicTrack? Current
    {
        get
        {
            lock (_gate)
            {
                return _position >= 0 && _position < _order.Count ? _tracks[_order[_position]] : null;
            }
        }
    }

    private bool _shuffle;

    /// <summary>Whether the playlist plays in a shuffled order. Changing it reshuffles.</summary>
    public bool Shuffle
    {
        get { lock (_gate) { return _shuffle; } }
        set
        {
            lock (_gate)
            {
                _shuffle = value;
                _orderIsStale = true;
            }
        }
    }

    /// <summary>Adds a track to the end.</summary>
    /// <param name="track">The track to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    public void Add(MusicTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);

        lock (_gate)
        {
            _tracks.Add(track);
            _orderIsStale = true;
        }
    }

    /// <summary>Removes a track.</summary>
    /// <param name="track">The track to remove.</param>
    /// <returns><see langword="true"/> if it was in the playlist.</returns>
    public bool Remove(MusicTrack track)
    {
        lock (_gate)
        {
            if (!_tracks.Remove(track))
            {
                return false;
            }

            _orderIsStale = true;
            return true;
        }
    }

    /// <summary>Removes every track.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _tracks.Clear();
            _order.Clear();
            _position = -1;
            _orderIsStale = true;
        }
    }

    /// <summary>Rewinds to before the first track, rebuilding the play order.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _position = -1;
            _orderIsStale = true;
        }
    }

    /// <summary>
    /// Advances to the next track according to <see cref="RepeatMode"/> and <see cref="Shuffle"/>.
    /// </summary>
    /// <returns>The next track, or <see langword="null"/> when the playlist has finished.</returns>
    public MusicTrack? MoveNext()
    {
        lock (_gate)
        {
            if (_tracks.Count == 0)
            {
                return null;
            }

            if (RepeatMode == MusicRepeatMode.One && _position >= 0 && _position < _order.Count)
            {
                return _tracks[_order[_position]];
            }

            EnsureOrderLocked();

            _position++;

            if (_position >= _order.Count)
            {
                if (RepeatMode != MusicRepeatMode.All)
                {
                    _position = _order.Count;
                    return null;
                }

                // Wrapping is where a reshuffle happens, and where repeating the track the player
                // just heard would be most obvious.
                var lastPlayed = _order.Count > 0 ? _order[^1] : -1;
                _position = 0;
                _orderIsStale = true;
                EnsureOrderLocked();

                if (_shuffle && _order.Count > 1 && _order[0] == lastPlayed)
                {
                    (_order[0], _order[1]) = (_order[1], _order[0]);
                }
            }

            return _tracks[_order[_position]];
        }
    }

    /// <summary>Steps back to the previous track.</summary>
    /// <returns>The previous track, or <see langword="null"/> if there is none.</returns>
    public MusicTrack? MovePrevious()
    {
        lock (_gate)
        {
            if (_tracks.Count == 0)
            {
                return null;
            }

            EnsureOrderLocked();

            if (_position <= 0)
            {
                if (RepeatMode != MusicRepeatMode.All)
                {
                    return null;
                }

                _position = _order.Count;
            }

            _position--;
            return _tracks[_order[_position]];
        }
    }

    // Callers hold _gate.
    private void EnsureOrderLocked()
    {
        if (!_orderIsStale)
        {
            return;
        }

        _order.Clear();
        for (var i = 0; i < _tracks.Count; i++)
        {
            _order.Add(i);
        }

        if (_shuffle)
        {
            // Fisher-Yates, from the seeded stream so a test can pin the order.
            for (var i = _order.Count - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
        }

        _orderIsStale = false;
    }
}
