namespace CodeBrix.Platform.GameEngine.CardsAndDice.Layout;

/// <summary>Behavior when a card is dropped into a full single-card area.</summary>
public enum OccupiedDropRule
{
    /// <summary>Refuse the drop.</summary>
    Reject,
    /// <summary>Place the incoming card on top.</summary>
    Stack,
    /// <summary>Send the occupant back to the source area.</summary>
    Swap,
    /// <summary>Send the occupant into the area's displacement pile.</summary>
    Displace
}
