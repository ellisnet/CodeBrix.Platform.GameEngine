using System;
using System.Drawing;
using CodeBrix.Platform.GameEngine.CardsAndDice.Table;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;

namespace CodeBrix.Platform.GameEngine.CardsAndDice;

/// <summary>One-call attachment to an existing engine scene or view.</summary>
public static class EngineCardsAndDiceExtensions
{
    /// <summary>Creates a screen-space table; the host must already have its input adapter configured.</summary>
    public static CardsAndDiceTable UseCardsAndDice(this Engine engine, RenderSurfaceHostBase host, View view, Rectangle bounds, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(engine); ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(view);
        var table = new CardsAndDiceTable(seed); table.Attach(engine, host, view, bounds); return table;
    }
    /// <summary>Creates a world-space table on a scene layer, picking through the specified view.</summary>
    public static CardsAndDiceTable UseCardsAndDice(this Engine engine, RenderSurfaceHostBase host, View view, SceneLayer layer, Rectangle bounds, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(engine); ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(view); ArgumentNullException.ThrowIfNull(layer);
        var table = new CardsAndDiceTable(seed); table.Attach(engine, host, view, layer, bounds); return table;
    }
}
