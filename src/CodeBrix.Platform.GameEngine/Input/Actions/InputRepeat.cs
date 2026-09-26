using System;

namespace CodeBrix.Platform.GameEngine.Input.Actions; //CodeBrix (not from Gondwana)

/// <summary>
/// Hold-to-repeat timing for an action (<see cref="InputActionMap.SetRepeat"/>): the press acts at once, then -
/// while the action stays held - again after <see cref="DelaySeconds"/> and every <see cref="IntervalSeconds"/>
/// after that, at a constant rate, until it is released. Menus typically use about 0.35 s then 0.1 s.
/// </summary>
public readonly record struct InputRepeat
{
    /// <summary>Creates repeat timing.</summary>
    /// <param name="delaySeconds">Seconds from the press to the first repeat; zero or more.</param>
    /// <param name="intervalSeconds">Seconds between repeats; above zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range or not a number.</exception>
    public InputRepeat(double delaySeconds, double intervalSeconds)
    {
        if (!(delaySeconds >= 0) || double.IsInfinity(delaySeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(delaySeconds), delaySeconds, "The delay must be zero or more seconds.");
        }

        if (!(intervalSeconds > 0) || double.IsInfinity(intervalSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), intervalSeconds, "The interval must be above zero seconds.");
        }

        DelaySeconds = delaySeconds;
        IntervalSeconds = intervalSeconds;
    }

    /// <summary>Seconds from the press to the first repeat.</summary>
    public double DelaySeconds { get; }

    /// <summary>Seconds between repeats.</summary>
    public double IntervalSeconds { get; }
}
