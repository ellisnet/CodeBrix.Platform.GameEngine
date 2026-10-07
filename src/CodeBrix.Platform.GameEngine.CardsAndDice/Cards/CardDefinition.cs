using System;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Cards;

/// <summary>Immutable artwork and game data shared by any number of physical cards.</summary>
public sealed record CardDefinition
{
    /// <summary>Creates a custom definition. Artwork keys refer to the bundled or table-registered SVG catalog.</summary>
    public CardDefinition(string key, string name, string face, string back = "backs/royal.svg", int value = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(face);
        ArgumentException.ThrowIfNullOrWhiteSpace(back);
        Key = key; Name = name; Face = face; Back = back; Value = value;
    }
    /// <summary>Definition key; instances remain distinct even when this key is shared.</summary>
    public string Key { get; }
    /// <summary>Display name.</summary>
    public string Name { get; }
    /// <summary>Face artwork key.</summary>
    public string Face { get; }
    /// <summary>Back artwork key.</summary>
    public string Back { get; }
    /// <summary>Optional game-defined numeric value.</summary>
    public int Value { get; }
    /// <summary>Optional game-specific immutable data.</summary>
    public object? Data { get; init; }
}
