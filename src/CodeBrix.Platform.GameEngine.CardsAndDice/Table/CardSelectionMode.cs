namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

/// <summary>Built-in click selection policy. Dragging remains independently configurable.</summary>
public enum CardSelectionMode
{
    /// <summary>The game handles CardClicked itself.</summary>
    None,
    /// <summary>Click toggles one card, clearing other selections on this table.</summary>
    Single,
    /// <summary>Click toggles a card without clearing other selections.</summary>
    Multiple
}
