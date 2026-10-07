using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Dice;

/// <summary>An n-sided die with independent logical results and optional custom faces.</summary>
public sealed class Die
{
    /// <summary>Creates a numbered die; results range from one through sides.</summary>
    public Die(int sides = 6) : this(Numbered(sides)) { }
    /// <summary>Creates a die with equally likely custom faces.</summary>
    public Die(IEnumerable<DieFace> faces)
    {
        ArgumentNullException.ThrowIfNull(faces);
        var items = faces.ToArray();
        if (items.Length < 2 || items.Any(f => f is null)) throw new ArgumentException("A die needs at least two non-null faces.", nameof(faces));
        Faces = Array.AsReadOnly(items);
    }
    private static IEnumerable<DieFace> Numbered(int sides)
    {
        if (sides < 2) throw new ArgumentOutOfRangeException(nameof(sides));
        return Enumerable.Range(1, sides).Select(i => new DieFace(i.ToString(System.Globalization.CultureInfo.InvariantCulture), i));
    }
    /// <summary>Creates a traditional six-sided die with complete dotted SVG faces.</summary>
    public static Die Traditional() => new(Enumerable.Range(1, 6).Select(i => new DieFace(i.ToString(System.Globalization.CultureInfo.InvariantCulture), i, $"dice/pips/{i}.svg"))) { HasPips = true };
    /// <summary>Whether this die uses the bundled full-size traditional pip faces.</summary>
    public bool HasPips { get; private init; }
    /// <summary>Unique identity.</summary>
    public Guid Id { get; } = Guid.NewGuid();
    /// <summary>All equally likely faces.</summary>
    public IReadOnlyList<DieFace> Faces { get; }
    /// <summary>Most recently rolled face index, or null before the first roll.</summary>
    public int? ResultIndex { get; private set; }
    /// <summary>Most recently rolled face, or null before the first roll.</summary>
    public DieFace? Result => ResultIndex is int i ? Faces[i] : null;
    /// <summary>Held dice are skipped by group rerolls.</summary>
    public bool IsHeld { get; set; }
    /// <summary>Rolls this die even if held. Group rolls honor the hold flag.</summary>
    public DieFace Roll(Random random)
    {
        ArgumentNullException.ThrowIfNull(random); ResultIndex = random.Next(Faces.Count); return Result!;
    }
    /// <summary>Creates a tens die for percentile pairs, labeled 00 through 90.</summary>
    public static Die PercentileTens() => new(Enumerable.Range(0, 10).Select(i => new DieFace((i * 10).ToString("00"), i * 10)));
    /// <summary>Creates a units die for percentile pairs, labeled 0 through 9.</summary>
    public static Die PercentileUnits() => new(Enumerable.Range(0, 10).Select(i => new DieFace(i.ToString(), i)));
    /// <summary>Combines a rolled percentile pair; 00 + 0 means 100.</summary>
    public static int PercentileResult(Die tens, Die units)
    {
        ArgumentNullException.ThrowIfNull(tens); ArgumentNullException.ThrowIfNull(units);
        if (!tens.Faces.Select(f => f.Value).SequenceEqual(Enumerable.Range(0, 10).Select(i => i * 10)) ||
            !units.Faces.Select(f => f.Value).SequenceEqual(Enumerable.Range(0, 10))) throw new ArgumentException("Expected a percentile tens/units pair.");
        if (tens.Result is null || units.Result is null) throw new InvalidOperationException("Roll both dice first.");
        int value = tens.Result.Value + units.Result.Value; return value == 0 ? 100 : value;
    }
}
