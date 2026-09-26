namespace CodeBrix.Platform.GameEngine.Input.Actions; //CodeBrix (not from Gondwana)

/// <summary>
/// One stick axis read as a digital direction (-1, 0 or +1): on past the press threshold, off again only inside
/// the release threshold (hysteresis), and after a release it cannot turn on again - either way - until the settle
/// time has passed, so the spring-back overshoot of a released stick is never read as a push the other way.
/// </summary>
internal sealed class DigitalStickAxis
{
    private double _sinceRelease = double.MaxValue;

    /// <summary>The direction now: -1, 0 or +1.</summary>
    public int Direction { get; private set; }

    /// <summary>Starts from a first reading: a stick already pushed past the release threshold counts as on.</summary>
    /// <param name="value">The axis value.</param>
    /// <param name="release">The release threshold.</param>
    public void Start(double value, double release)
    {
        Direction = value >= release ? 1 : value <= -release ? -1 : 0;
        _sinceRelease = double.MaxValue;
    }

    /// <summary>Takes one reading.</summary>
    /// <param name="value">The axis value.</param>
    /// <param name="elapsed">Seconds since the previous reading.</param>
    /// <param name="press">The press threshold.</param>
    /// <param name="release">The release threshold.</param>
    /// <param name="settle">The settle time after a release, in seconds.</param>
    public void Update(double value, double elapsed, double press, double release, double settle)
    {
        if ((Direction > 0 && value < release) || (Direction < 0 && value > -release))
        {
            Direction = 0;
            _sinceRelease = 0;
            return;
        }

        if (Direction != 0)
        {
            return;
        }

        if (_sinceRelease < double.MaxValue)
        {
            _sinceRelease += elapsed > 0 ? elapsed : 0;
        }

        if (_sinceRelease < settle)
        {
            return;
        }

        Direction = value >= press ? 1 : value <= -press ? -1 : 0;
    }
}
