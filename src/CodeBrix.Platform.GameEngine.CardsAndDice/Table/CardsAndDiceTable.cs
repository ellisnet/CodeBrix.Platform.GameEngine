using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.CardsAndDice.Assets;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using CodeBrix.Platform.GameEngine.CardsAndDice.Dice;
using CodeBrix.Platform.GameEngine.CardsAndDice.Layout;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace CodeBrix.Platform.GameEngine.CardsAndDice.Table;

/// <summary>
/// Animated cards and dice with optional engine wiring. Mutate on the engine thread, or before start.
/// Dispose after rendering has stopped so published frames no longer reference its images.
/// </summary>
public sealed partial class CardsAndDiceTable : IDisposable
{
    /// <summary>Duration of the embedded Vorbis shuffle clip, in seconds. Shuffles use real time to stay synchronized with audio.</summary>
    public const double ShuffleDurationSeconds = 3.063492;

    private sealed class Visual
    {
        public required Card Card;
        public Vector2 Position, Target;
        public float Width, Height, Angle, TargetAngle;
        public bool Face;
        public double FlipLeft;
    }
    private readonly List<TableArea> _areas = [];
    private readonly List<TableDie> _dice = [];
    private readonly Dictionary<Guid, Visual> _visuals = [];
    private readonly Dictionary<string, string> _custom = [];
    // internal (not private) so the test assembly can count cached images through the InternalsVisibleTo seam.
    internal readonly Dictionary<string, SKImage> _images = [];
    private readonly HashSet<string> _sounds = [];
    private readonly string _soundPrefix = "cardsdice." + Guid.NewGuid().ToString("N") + ".";
    private readonly List<Visual> _drawOrder = [];
    private Engine? _engine;
    private DrawListDrawing? _drawing;
    private View? _view;
    private SceneLayer? _layer;
    private double _lastTime;
    private bool _disposed, _wasBusy;
    private Visual? _drag;
    private Vector2 _grab, _press, _pointer;
    private bool _dragMoved;
    private TableArea? _dropHighlight;
    private float _speed = 1;
    private readonly DrawList _list = new();
    /// <summary>Creates an unattached table; useful for headless rules and manual DrawList composition.</summary>
    public CardsAndDiceTable(int? seed = null) => Random = seed.HasValue ? new(seed.Value) : new();
    /// <summary>Random source for logical dice outcomes. Animation never consumes it.</summary>
    public Random Random { get; }
    /// <summary>Read-only table areas.</summary>
    public IReadOnlyList<TableArea> Areas => _areas.AsReadOnly();
    /// <summary>Read-only dice placements.</summary>
    public IReadOnlyList<TableDie> Dice => _dice.AsReadOnly();
    /// <summary>Removes animation travel and completes actions immediately when enabled.</summary>
    public bool ReducedMotion { get; set; }
    /// <summary>Positive animation speed multiplier.</summary>
    public float AnimationSpeed { get => _speed; set { if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); _speed = value; } }
    /// <summary>Opt-in sound effects, decoded from embedded streams on first use.</summary>
    public bool SoundEnabled { get; set; }
    /// <summary>Enables card dragging. Click and inspection events remain available.</summary>
    public bool DragEnabled { get; set; } = true;
    /// <summary>Disables pointer interaction, for example while the game displays a modal inspector.</summary>
    public bool InputEnabled { get; set; } = true;
    /// <summary>Whether cards are moving/flipping or dice are rolling.</summary>
    public bool IsAnimating => IsDealing || _areas.Any(a => a.ShuffleLeft > 0) || _dice.Any(d => d.RollLeft > 0) || _visuals.Values.Any(v => v.FlipLeft > 0 || Vector2.DistanceSquared(v.Position, v.Target) > .25f || Math.Abs(v.Angle - v.TargetAngle) > .1f);
    /// <summary>Raised on a card click; the game chooses what selection means.</summary>
    public event Action<Card>? CardClicked;
    /// <summary>Raised on a die click; the default behavior toggles its hold flag first.</summary>
    public event Action<Die>? DieClicked;
    /// <summary>Raised on right click for inspection.</summary>
    public event Action<Card>? InspectRequested;
    /// <summary>Raised after a successful drop.</summary>
    public event Action<Card, TableArea>? CardDropped;
    /// <summary>Raised once when all current animations settle.</summary>
    public event Action? AnimationsCompleted;
    /// <summary>Raised after rolling, with the logical faces already chosen.</summary>
    public event Action<IReadOnlyList<DieFace>>? DiceRolled;

    /// <summary>Registers custom SVG text under a unique key. No disk access is performed.</summary>
    public void RegisterSvg(string key, string svg)
    {
        Check(); ArgumentException.ThrowIfNullOrWhiteSpace(key); ArgumentException.ThrowIfNullOrWhiteSpace(svg);
        if (_custom.ContainsKey(key) || AssetCatalog.All.Any(a => a.Key == key)) throw new ArgumentException("Artwork key already exists.", nameof(key));
        // Parse now so invalid custom art fails during setup rather than inside a frame.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svg)); using var resource = SvgResource.Load(stream);
        var image = Rasterize(resource, key);
        _custom.Add(key, svg); _images.Add(key, image);
    }
    /// <summary>Registers custom SVG from a caller-owned stream.</summary>
    public void RegisterSvg(string key, Stream svg)
    {
        ArgumentNullException.ThrowIfNull(svg); using var reader = new StreamReader(svg, leaveOpen: true); RegisterSvg(key, reader.ReadToEnd());
    }
    /// <summary>Adds a pile, automatically handling its layout and card movement.</summary>
    public TableArea AddArea(CardPile pile, SKRect bounds, CardLayout layout = CardLayout.Fan)
    {
        Check(); ArgumentNullException.ThrowIfNull(pile); ValidateRect(bounds);
        if (_areas.Any(a => ReferenceEquals(a.Pile, pile))) throw new ArgumentException("This pile already has an area.", nameof(pile));
        var area = new TableArea(pile, bounds, layout); _areas.Add(area); Synchronize(true); return area;
    }
    /// <summary>Adds a die at a center position.</summary>
    public TableDie AddDie(Die die, Vector2 center, float size = 100)
    {
        Check(); ArgumentNullException.ThrowIfNull(die);
        if (!float.IsFinite(size) || size <= 0 || !float.IsFinite(center.X) || !float.IsFinite(center.Y)) throw new ArgumentOutOfRangeException(nameof(size));
        if (_dice.Any(d => ReferenceEquals(d.Die, die))) throw new ArgumentException("Die already placed.", nameof(die));
        var item = new TableDie(die, center, size); _dice.Add(item); return item;
    }
    /// <summary>Clears placement and interaction state. Logical piles and dice remain usable.</summary>
    public void Clear()
    {
        Check(); CancelPreparation(); CancelDeals(); CancelDrag(); _areas.Clear(); _dice.Clear(); _visuals.Clear(); _drawOrder.Clear(); _wasBusy = false;
    }
    /// <summary>
    /// Prepares both sides of cards in the table-owned image cache. Call during setup/loading,
    /// on the engine thread, before animations start. Repeated keys reuse the cached image.
    /// </summary>
    public void PrepareCards(IEnumerable<Card> cards)
    {
        Check(); ArgumentNullException.ThrowIfNull(cards);
        foreach (var card in cards)
        {
            ArgumentNullException.ThrowIfNull(card);
            Image(card.Definition.Back); Image(card.Definition.Face);
        }
    }
    /// <summary>Shuffles a pile logically, then animates a riffle.</summary>
    public void Shuffle(CardPile pile, Random? random = null)
    {
        Check(); ArgumentNullException.ThrowIfNull(pile);
        var area = _areas.FirstOrDefault(a => ReferenceEquals(a.Pile, pile));
        if (area?.ShuffleLeft > 0) return;
        PrepareCards(pile.Cards);
        bool alreadyShuffling = _areas.Any(a => a.ShuffleLeft > 0);
        pile.Shuffle(random ?? Random); if (area != null && pile.Count > 1)
        {
            CancelDrag();
            area.ShuffleLeft = ReducedMotion ? 0 : ShuffleDurationSeconds;
            _wasBusy |= !ReducedMotion;
            if (!ReducedMotion && !alreadyShuffling) Play("card-shuffle.ogg");
        }
    }
    /// <summary>Deals cards and animates their movement from the draw pile to the target hand.</summary>
    public IReadOnlyList<Card> Deal(Deck deck, CardPile hand, int count = 1, bool faceUp = true)
    {
        Check(); ArgumentNullException.ThrowIfNull(deck); ArgumentNullException.ThrowIfNull(hand);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        PrepareCards(deck.DrawPile.Cards.Take(count));
        Synchronize(false); var dealt = deck.Deal(count, hand, false); Synchronize(false);
        foreach (var card in dealt) if (faceUp) Flip(card, true);
        if (dealt.Count > 0) Play("card-slide-1.ogg"); _wasBusy |= IsAnimating; return dealt;
    }
    /// <summary>Changes facing with a centered horizontal flip, independent of rotation.</summary>
    public void Flip(Card card, bool? faceUp = null)
    {
        Check(); ArgumentNullException.ThrowIfNull(card); bool target = faceUp ?? !card.IsFaceUp;
        if (target == card.IsFaceUp) return;
        Image(target ? card.Definition.Face : card.Definition.Back);
        card.IsFaceUp = target;
        if (_visuals.TryGetValue(card.Id, out var v)) { v.FlipLeft = ReducedMotion ? 0 : .3; if (ReducedMotion) v.Face = target; }
        _wasBusy |= IsAnimating; Play("card-place-1.ogg");
    }
    /// <summary>Rotates a card without changing its face.</summary>
    public void Rotate(Card card, float degrees = 180)
    {
        Check(); ArgumentNullException.ThrowIfNull(card); if (!float.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
        card.Rotation = (card.Rotation + degrees) % 360;
    }
    /// <summary>Rolls all unheld dice and starts their visual tumbling.</summary>
    public IReadOnlyList<DieFace> Roll()
    {
        Check(); var result = new List<DieFace>();
        foreach (var d in _dice) if (!d.Die.IsHeld) { result.Add(d.Die.Roll(Random)); d.RollLeft = ReducedMotion ? 0 : 1.0; }
        _wasBusy |= IsAnimating; if (result.Count > 0) Play("dice-shake-1.ogg");
        var view = result.AsReadOnly(); DiceRolled?.Invoke(view); return view;
    }
    /// <summary>Attempts a drop, applying all acceptance rules before moving any cards.</summary>
    public bool TryDrop(Card card, TableArea target)
    {
        Check(); ArgumentNullException.ThrowIfNull(card); ArgumentNullException.ThrowIfNull(target);
        if (!_areas.Contains(target) || !target.AcceptsDrops || target.CanDrop?.Invoke(card) == false) return false;
        if (ReferenceEquals(card.Pile, target.Pile)) return true;
        var occupant = target.SingleCard ? target.Pile.Peek() : null;
        if (occupant != null)
        {
            switch (target.OccupiedRule)
            {
                case OccupiedDropRule.Reject: return false;
                case OccupiedDropRule.Swap:
                    if (card.Pile is null) return false;
                    var source = _areas.FirstOrDefault(a => ReferenceEquals(a.Pile, card.Pile));
                    if (source != null && (!source.AcceptsDrops || source.CanDrop?.Invoke(occupant) == false)) return false;
                    card.Pile.Add(occupant);
                    break;
                case OccupiedDropRule.Displace:
                    if (target.DisplacementPile is null || ReferenceEquals(target.DisplacementPile, target.Pile)) return false;
                    target.DisplacementPile.Add(occupant); break;
                case OccupiedDropRule.Stack: break;
                default: throw new ArgumentOutOfRangeException(nameof(target));
            }
        }
        target.Pile.Add(card, onTop: target.Layout == CardLayout.Stack); CardDropped?.Invoke(card, target); Play("card-place-1.ogg"); return true;
    }
    /// <summary>Advances presentation in seconds. An attached table calls this automatically using pause-safe engine time.</summary>
    public void Update(double seconds)
    {
        Check(); if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var preparation in _preparations) preparation.Pump(_images);
        _preparations.RemoveAll(p => p.IsComplete);
        Synchronize(false); double dt = seconds * AnimationSpeed;
        double shuffleWait = _areas.Count == 0 ? 0 : _areas.Max(a => a.ShuffleLeft);
        foreach (var a in _areas) a.ShuffleLeft = ReducedMotion ? 0 : Math.Max(0, a.ShuffleLeft - seconds);
        foreach (var d in _dice) { bool rolling = d.RollLeft > 0; d.RollLeft = ReducedMotion ? 0 : Math.Max(0, d.RollLeft - dt); if (rolling && d.RollLeft == 0) Play("die-throw-1.ogg"); }
        if (ReducedMotion || !_areas.Any(a => a.ShuffleLeft > 0))
            AdvanceDeals(Math.Max(0, seconds - shuffleWait) * AnimationSpeed);
        foreach (var v in _visuals.Values)
        {
            if (ReferenceEquals(v, _flight?.Visual)) continue;
            if (v != _drag) { v.Position = ReducedMotion ? v.Target : Vector2.Lerp(v.Position, v.Target, (float)(1 - Math.Exp(-dt * 14))); if (Vector2.DistanceSquared(v.Position, v.Target) < .25f) v.Position = v.Target; }
            v.Angle = ReducedMotion ? v.TargetAngle : v.Angle + (v.TargetAngle - v.Angle) * (float)(1 - Math.Exp(-dt * 14));
            v.FlipLeft = ReducedMotion ? 0 : Math.Max(0, v.FlipLeft - dt);
            if (v.FlipLeft <= .15) v.Face = v.Card.IsFaceUp;
        }
        bool busy = IsAnimating;
        if (_wasBusy && !busy) { _wasBusy = false; AnimationsCompleted?.Invoke(); } else _wasBusy = busy;
    }
    /// <summary>Completes all current visual motion without changing logical outcomes.</summary>
    public void CompleteAnimations()
    {
        Check(); bool reduced = ReducedMotion; ReducedMotion = true; Update(0); ReducedMotion = reduced;
    }
    private void Synchronize(bool snap)
    {
        var present = new HashSet<Guid>();
        foreach (var area in _areas)
        {
            ValidateRect(area.Bounds);
            var poses = CardLayouts.Arrange(area.Layout, area.Pile.Count, area.Bounds.Left, area.Bounds.Top, area.Bounds.Width, area.Bounds.Height, area.CardWidth, area.CardHeight);
            for (int i = 0; i < area.Pile.Count; i++)
            {
                var card = area.Pile.Cards[i]; present.Add(card.Id);
                var pose = area.Layout == CardLayout.Manual && area.ManualPoses.TryGetValue(card.Id, out var manual) ? manual : poses[i];
                if (!_visuals.TryGetValue(card.Id, out var v)) _visuals.Add(card.Id, v = new() { Card = card, Position = pose.Center, Face = card.IsFaceUp, Angle = pose.Rotation + card.Rotation });
                v.Target = pose.Center; v.Width = area.CardWidth; v.Height = area.CardHeight; v.TargetAngle = pose.Rotation + card.Rotation;
                if (snap) v.Position = v.Target;
            }
        }
        foreach (var id in _visuals.Keys.Where(id => !present.Contains(id)).ToArray()) _visuals.Remove(id);
    }
    /// <summary>Appends current tabletop visuals to a caller-owned draw list.</summary>
    public void Draw(DrawList list)
    {
        Check(); ArgumentNullException.ThrowIfNull(list); Synchronize(false); _drawOrder.Clear();
        foreach (var a in _areas)
        {
            list.Rectangle(a.Bounds, new SKColor(12, 25, 34, 70), ReferenceEquals(a, _dropHighlight) ? SKColors.Gold : new SKColor(111, 142, 141, 90), 1, 12);
            if (a.ShuffleLeft > 0) { DrawShuffle(list, a); continue; }
            IEnumerable<Card> cards = a.Layout == CardLayout.Stack ? a.Pile.Cards.Take(4).Reverse() : a.Pile.Cards;
            foreach (var card in cards) if (_visuals.TryGetValue(card.Id, out var v) && v != _drag && !ReferenceEquals(v, _flight?.Visual))
            {
                DrawCard(list, v, 0); _drawOrder.Add(v);
            }
        }
        foreach (var d in _dice) DrawDie(list, d);
        if (_flight != null) DrawCard(list, _flight.Visual, 0);
        if (_drag != null) { DrawCard(list, _drag, 0); _drawOrder.Add(_drag); }
    }
    private static float Ease(double value)
    {
        float t = (float)Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
    private void DrawShuffle(DrawList list, TableArea area)
    {
        // Show representative cards as two packets, then riffle them together one by one.
        // These are presentation poses only: logical card ownership and RNG stay untouched.
        double progress = 1 - area.ShuffleLeft / ShuffleDurationSeconds;
        float lift = Ease(progress / .12) * (1 - Ease((progress - .86) / .14));
        float split = Ease((progress - .12) / .20);
        float square = Ease((progress - .76) / .10);
        int count = Math.Min(16, area.Pile.Count);
        for (int layer = count - 1; layer >= 0; layer--)
        {
            // The right packet's top card is fed last and drawn over the left top card.
            // Swapping these two layers while packets are separated avoids a visible z-order pop.
            int i = count >= 2 && progress > .70 && layer < 2 ? 1 - layer : layer;
            // The card that finishes on top must also be the logical top card,
            // including decks with different backs or face-up shuffling.
            int cardIndex = count >= 2 && i < 2 ? 1 - i : i;
            var card = area.Pile.Cards[cardIndex];
            float side = i % 2 == 0 ? -1 : 1;
            int feedOrder = i == 0 ? count - 2 : i == 1 ? count - 1 : count - 1 - i;
            float feed = Ease(((progress - .34) / .42 * count - feedOrder) / 1.4);
            float spread = side * area.CardWidth * .62f * split * (1 - feed);
            float depth = Math.Min(i, 7) * 2f;
            float x = area.Bounds.MidX + area.CardWidth * .65f * lift + spread + depth * .30f * (1 - square);
            float y = area.Bounds.MidY - 22 * lift - depth * (1 - square)
                - 16 * MathF.Sin(feed * MathF.PI) * split;
            float angle = side * 13 * split * (1 - feed);
            list.Rectangle(x + 5, y + 8, area.CardWidth, area.CardHeight, new SKColor(0, 0, 0, 65), cornerRadius: 8, rotation: angle);
            list.Image(Image(card.IsFaceUp ? card.Definition.Face : card.Definition.Back), x, y,
                area.CardWidth, area.CardHeight, angle, fit: DrawImageFit.Stretch);
        }
    }
    private void DrawCard(DrawList list, Visual v, float shuffle)
    {
        float lift = v == _drag ? 0 : v.Card.IsSelected ? 14 : 0;
        float x = v.Position.X + shuffle, y = v.Position.Y - lift;
        float sx = v.FlipLeft > 0 ? Math.Max(.015f, Math.Abs((float)Math.Cos((1 - v.FlipLeft / .3) * Math.PI))) : 1;
        float width = v.Width * sx;
        list.Rectangle(x + 5, y + 8, width, v.Height, new SKColor(0, 0, 0, 90), cornerRadius: 8, rotation: v.Angle);
        if (v.Card.IsSelected || v == _drag || Hit(v, _pointer)) list.Rectangle(x, y, width + 6, v.Height + 6, SKColors.Transparent, SKColors.Gold, 2, 8, rotation: v.Angle);
        list.Image(Image(v.Face ? v.Card.Definition.Face : v.Card.Definition.Back), x, y, width, v.Height, v.Angle, fit: DrawImageFit.Stretch);
    }
    private void DrawDie(DrawList list, TableDie d)
    {
        double t = d.RollLeft; float x = d.Center.X + (float)(Math.Sin(t * 35) * 25 * t), y = d.Center.Y - (float)(Math.Abs(Math.Sin(t * 19)) * 50 * t);
        int index = t > 0 ? (int)(t * 23) % d.Die.Faces.Count : d.Die.ResultIndex ?? 0;
        var face = d.Die.Faces[index]; string shape = $"dice/d{d.Die.Faces.Count}.svg";
        if (!AssetCatalog.All.Any(a => a.Key == shape)) shape = "dice/numbered.svg";
        float angle = (float)(t * 700);
        list.Circle(x + 4, d.Center.Y + d.Size * .35, d.Size * .38, new SKColor(0, 0, 0, 80));
        if (d.Die.IsHeld) list.Circle(x, y, d.Size * .60, new SKColor(0, 0, 0, 0), SKColors.Gold, 3);
        if (d.Die.HasPips)
        {
            float tumble = t > 0 ? 1 - .18f * MathF.Abs(MathF.Sin((float)t * 19)) : 1;
            list.Image(Image(face.Artwork!), x, y, d.Size * tumble, d.Size, angle, fit: DrawImageFit.Stretch);
        }
        else
        {
            list.Image(Image(shape), x, y, d.Size, d.Size, angle);
            if (face.Artwork is { } artwork) list.Image(Image(artwork), x, y, d.Size * .44, d.Size * .44, angle);
            else list.Text(d.Die.Result == null && t == 0 ? "?" : face.Label, x, y, SKTypeface.Default, d.Size * .29, SKColors.White, rotation: angle);
        }
        if (d.Die.IsHeld) list.Text("HELD", d.Center.X, d.Center.Y + d.Size * .78, SKTypeface.Default, 13, SKColors.Gold);
    }
    /// <summary>Gets a cached raster image of bundled or registered SVG. Table-owned; never dispose it yourself.</summary>
    public SKImage Image(string key)
    {
        Check(); if (_images.TryGetValue(key, out var image)) return image;
        using var stream = _custom.TryGetValue(key, out var svg) ? new MemoryStream(Encoding.UTF8.GetBytes(svg)) : AssetCatalog.Open(key);
        using var resource = SvgResource.Load(stream);
        image = Rasterize(resource, key); _images.Add(key, image); return image;
    }
    internal static SKImage Rasterize(SvgResource resource, string key)
    {
        float ratio = resource.IntrinsicSize.Height / resource.IntrinsicSize.Width;
        int width = key.StartsWith("symbols/") ? 144 : 480;
        using var bitmap = resource.Rasterize(width, Math.Clamp((int)(width * ratio), 1, 840)).Copy();
        return SKImage.FromBitmap(bitmap);
    }
    /// <summary>Handles pointer input in table coordinates. Returns whether the table consumed the event.</summary>
    public bool Pointer(Vector2 point, bool pressed = false, bool released = false, bool inspect = false)
    {
        Check(); if (!InputEnabled) { CancelDrag(); return false; }
        _pointer = point;
        if (_drag != null)
        {
            if (DragEnabled && CanDrag?.Invoke(_drag.Card) != false && Vector2.Distance(point, _press) > 4) _dragMoved = true;
            if (_dragMoved) { _drag.Position = point - _grab; _dropHighlight = _areas.LastOrDefault(a => a.AcceptsDrops && a.Bounds.Contains(point.X, point.Y)); }
            if (released)
            {
                var v = _drag; bool moved = _dragMoved; var target = _dropHighlight; CancelDrag();
                if (moved)
                {
                    if (target != null && TryDrop(v.Card, target) && target.Layout == CardLayout.Manual)
                        target.ManualPoses[v.Card.Id] = new(v.Position + new Vector2(0, v.Card.IsSelected ? 14 : 0), v.Angle - v.Card.Rotation);
                }
                else { SelectOnClick(v.Card); CardClicked?.Invoke(v.Card); }
            }
            return true;
        }
        var hit = _drawOrder.LastOrDefault(v => !ReferenceEquals(v, _flight?.Visual) && Hit(v, point));
        if (inspect && hit != null) { InspectRequested?.Invoke(hit.Card); return true; }
        if (!pressed) return false;
        if (hit != null)
        {
            _drag = hit; _press = point;
            hit.Position -= new Vector2(0, hit.Card.IsSelected ? 14 : 0);
            _grab = point - hit.Position; _dragMoved = false; return true;
        }
        var die = _dice.LastOrDefault(d => Math.Abs(point.X - d.Center.X) <= d.Size / 2 && Math.Abs(point.Y - d.Center.Y) <= d.Size / 2);
        if (die != null && die.RollLeft == 0) { die.Die.IsHeld = !die.Die.IsHeld; DieClicked?.Invoke(die.Die); return true; }
        return false;
    }
    private static bool Hit(Visual v, Vector2 p)
    {
        var d = p - (v.Position - new Vector2(0, v.Card.IsSelected ? 14 : 0)); double a = -v.Angle * Math.PI / 180;
        double x = d.X * Math.Cos(a) - d.Y * Math.Sin(a), y = d.X * Math.Sin(a) + d.Y * Math.Cos(a);
        return Math.Abs(x) <= v.Width / 2 && Math.Abs(y) <= v.Height / 2;
    }
    /// <summary>Cancels a drag, leaving ownership unchanged and returning the visual to its layout.</summary>
    public void CancelDrag() { _drag = null; _dragMoved = false; _dropHighlight = null; }
    internal void Attach(Engine engine, RenderSurfaceHostBase host, View view, Rectangle bounds)
    {
        Check(); _engine = engine; _view = view; _drawing = new(host, view, bounds, _list, "CardsAndDice"); Wire();
    }
    internal void Attach(Engine engine, RenderSurfaceHostBase host, View view, SceneLayer layer, Rectangle bounds)
    {
        Check(); _engine = engine; _view = view; _layer = layer; _drawing = new(host, layer, bounds, _list, "CardsAndDice"); Wire();
    }
    private void Wire()
    {
        _lastTime = _engine!.TotalSecondsEngineRunning; _engine.BeforeFrameRender += Frame; _engine.Paused += CancelDrag; _engine.Disposing += Dispose;
        _engine.Input.MouseEventPoller!.MouseEvent += Mouse; _engine.Input.MouseEventPoller!.StartMonitoringMouse();
    }
    private void Mouse(MouseEventArgs e)
    {
        var p = e.CurrentPosition;
        Vector2 point = new(p.X, p.Y);
        if (_layer != null) { var world = _view!.ScreenPxToWorldPx(_layer, p); point = new(world.X, world.Y); }
        Pointer(point, e.LeftButtonJustPressed, e.LeftButtonJustReleased, e.RightButtonJustPressed);
    }
    private void Frame()
    {
        double now = _engine!.TotalSecondsEngineRunning; Update(Math.Max(0, now - _lastTime)); _lastTime = now;
        _list.Clear(); Draw(_list); _list.Publish();
    }
    private void Play(string name)
    {
        if (!SoundEnabled) return;
        string key = _soundPrefix + name;
        try
        {
            if (_sounds.Add(key)) { using var stream = AssetCatalog.Open("sounds/" + name); AudioResourceManager.Instance.LoadFromStream(key, stream, ".ogg"); }
            AudioResourceManager.Instance.TryPlaySfx(key, .5f);
        }
        catch (Exception ex) { SoundEnabled = false; Engine.Logger.LogWarning(ex, "CardsAndDice sound disabled after audio failure."); }
    }
    private static void ValidateRect(SKRect r)
    {
        if (!float.IsFinite(r.Left) || !float.IsFinite(r.Top) || !float.IsFinite(r.Right) || !float.IsFinite(r.Bottom) || r.Width <= 0 || r.Height <= 0) throw new ArgumentOutOfRangeException(nameof(r));
    }
    private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);
    /// <summary>Detaches input and frame hooks, then releases owned drawing, image, and audio resources.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        if (_engine != null) { _engine.BeforeFrameRender -= Frame; _engine.Paused -= CancelDrag; _engine.Disposing -= Dispose; _engine.Input.MouseEventPoller!.MouseEvent -= Mouse; }
        CancelPreparation(); CancelDeals(); CancelDrag(); _drawing?.Dispose(); _list.Clear(); _list.Publish();
        foreach (var image in _images.Values) image.Dispose(); _images.Clear();
        foreach (var sound in _sounds) AudioResourceManager.Instance.Unload(sound);
        _sounds.Clear(); _disposed = true;
    }
}
