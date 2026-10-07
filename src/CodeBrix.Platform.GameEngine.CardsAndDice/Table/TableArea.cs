using System;
using System.Collections.Generic;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using CodeBrix.Platform.GameEngine.CardsAndDice.Layout;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

/// <summary>A named region displaying a pile. Coordinates are in the table's drawing space.</summary>
public sealed class TableArea
{
    internal TableArea(CardPile pile, SKRect bounds, CardLayout layout) { Pile = pile; Bounds = bounds; Layout = layout; }
    /// <summary>Cards displayed in this area.</summary>
    public CardPile Pile { get; }
    /// <summary>Layout and drop-target rectangle.</summary>
    public SKRect Bounds { get; set; }
    /// <summary>Layout style.</summary>
    public CardLayout Layout { get; set; }
    /// <summary>Displayed card width.</summary>
    public float CardWidth { get; set; } = 110;
    /// <summary>Displayed card height.</summary>
    public float CardHeight { get; set; } = 168;
    /// <summary>Whether dragged cards may be dropped here.</summary>
    public bool AcceptsDrops { get; set; } = true;
    /// <summary>Optional game rule, evaluated before any ownership changes.</summary>
    public Func<Card, bool>? CanDrop { get; set; }
    /// <summary>Whether this is a single-card slot.</summary>
    public bool SingleCard { get; set; }
    /// <summary>Rule used for an occupied single-card slot.</summary>
    public OccupiedDropRule OccupiedRule { get; set; } = OccupiedDropRule.Reject;
    /// <summary>Destination for a displaced occupant.</summary>
    public CardPile? DisplacementPile { get; set; }
    /// <summary>Explicit poses used by Manual layout.</summary>
    public Dictionary<Guid, CardPose> ManualPoses { get; } = [];
    internal double ShuffleLeft;
}
