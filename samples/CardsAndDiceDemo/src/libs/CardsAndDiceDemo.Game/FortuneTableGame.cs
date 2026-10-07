using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.CardsAndDice.Assets;
using CodeBrix.Platform.GameEngine.CardsAndDice.Cards;
using CodeBrix.Platform.GameEngine.CardsAndDice.Dice;
using CodeBrix.Platform.GameEngine.CardsAndDice.Layout;
using CodeBrix.Platform.GameEngine.CardsAndDice.Table;
using CodeBrix.Platform.GameEngine.Drawing.Direct.DrawLists;
using CodeBrix.Platform.GameEngine.Host;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using CodeBrix.Platform.GameEngine.Scenes;
using SkiaSharp;

namespace CardsAndDiceDemo.Game;

/// <summary>A complete small game and interactive gallery using the public add-on API.</summary>
public sealed class FortuneTableGame : IDisposable
{
    private readonly GameSurfaceCanvas _canvas;
    private CardsAndDiceTable _table = null!;
    private readonly DrawList _hud = new();
    private DrawListDrawing _drawing = null!;
    private Scene _scene = null!;
    private Deck _deck = null!;
    private Deck? _bonusDeck;
    private CardPile _hand = new("Hand"), _bonus = new("Bonus");
    private int _mode, _stage, _round = 1, _score, _galleryPage;
    private int _diceSet;
    private readonly List<TableArea> _players = [];
    private TableArea? _dealerBoard;
    private DealSequence? _dealerSequence;
    private bool _dealAfterShuffle;
    private ArtworkPreparation? _preparation;
    private Action? _afterPreparation;
    private double _shuffleStartedAt;
    private int _shuffleSnapshot;
    private int _tableWidth = 1280;
    private float ExtraWidth => _tableWidth - 1280;
    private float CenterX => _tableWidth / 2f;
    private double _lastFrameTime;
    private string _message = "Choose a table, then deal or roll. Reach the target to win.";
    private string? _inspect;
    private static readonly string[] Modes = ["CLASSIC", "MINIMAL", "TAROT", "ELEMENTS", "DICE", "GALLERY", "DEALER"];
    private static readonly SKColor Gold = SKColor.Parse("#dfbb70"), Cream = SKColor.Parse("#f3ead4"), Muted = SKColor.Parse("#91aaa9");
    private bool _disposed;
    private int _walkStep;
    private double _walkAt;
    private bool _loadingCaptured;
    /// <summary>Creates the game for a platform canvas.</summary>
    public FortuneTableGame(GameSurfaceCanvas canvas) => _canvas = canvas;
    /// <summary>Starts the table when the canvas receives its first real size.</summary>
    public void InitializeGame(string? configPath = null, bool? autoSaveConfig = null)
    {
        _canvas.SetRenderResolution(1280, 800);
        var engine = Engine.Instance; engine.Initialize(configPath, autoSaveConfig ?? false);
        engine.InitializeCodeBrixMouseAdapter(_canvas); engine.InitializeCodeBrixKeyboardAdapter(_canvas);
        _scene = new Scene(); _scene.AddPixelLayer(1280, 800); _canvas.Host.Bind(_scene, false);
        _canvas.Host.Backbuffer.ClearColor = SKColor.Parse("#10292d");
        _table = new CardsAndDiceTable(seed: 2718);
        _table.SelectionMode = CardSelectionMode.Multiple;
        _table.CanSelect = card => (_mode == 6 && !ReferenceEquals(card.Pile, _deck.DrawPile)) || (_stage == 1 && ReferenceEquals(card.Pile, _hand));
        _table.CanDrag = card => !ReferenceEquals(card.Pile, _deck?.DrawPile);
        _table.CardClicked += card =>
        {
            if (_mode == 6 && !card.IsFaceUp && !ReferenceEquals(card.Pile, _deck.DrawPile))
            { card.IsSelected = false; _table.Flip(card); }
        };
        _table.InspectRequested += card => { _inspect = card.Definition.Face; _table.InputEnabled = false; };
        _drawing = new(_canvas.Host, _canvas.Host.ViewManager.Views[0], new Rectangle(0, 0, 1280, 800), _hud, "Fortune table controls") { ZOrder = 100 };
        RegisterElementArt(); Setup();
        engine.Input.MouseEventPoller!.MouseEvent += Mouse;
        engine.BeforeFrameRender += Frame; engine.Paused += _table.CancelDrag; engine.Disposing += Dispose;
        engine.Input.MouseEventPoller!.StartMonitoringMouse();
        engine.Configuration.TargetFPS = 60;
        engine.Start(SynchronizationContext.Current!);
    }
    private void RegisterElementArt()
    {
        foreach (string element in new[] { "fire", "water", "earth", "air" })
            for (int i = 1; i <= 6; i++) _table.RegisterSvg($"custom/{element}-{i}", CardComposer.Compose(element.ToUpperInvariant(), $"symbols/original/{element}.svg", i, "Gather the four elements", accent: element switch { "fire" => "#f29b75", "water" => "#80c6dd", "earth" => "#a2c586", _ => "#ddccf2" }));
    }
    private void Setup()
    {
        _afterPreparation = null; _preparation = null; _loadingCaptured = false;
        _dealAfterShuffle = false; _table.Clear(); _table.InputEnabled = true; _hand = new("Hand"); _bonus = new("Bonus"); _bonusDeck = null; _stage = 0; _score = 0; _inspect = null;
        _players.Clear(); _dealerBoard = null; _dealerSequence = null;
        _message = "Deal, choose cards to KEEP, then replace once or bank your score.";
        if (_mode == 6) { SetupDealer(); return; }
        if (_mode == 5) { _message = "Browse the embedded artwork. Click an asset to inspect it."; return; }
        if (_mode == 4)
        {
            int[][] sets = [[6, 6, 6, 6, 6], [4, 6, 8, 12, 20], [7, 7, 7, 7, 7], [10, 10]];
            var sides = sets[_diceSet];
            for (int i = 0; i < sides.Length; i++) _table.AddDie(_diceSet == 3 ? (i == 0 ? Die.PercentileTens() : Die.PercentileUnits()) : _diceSet == 0 ? Die.Traditional() : new Die(sides[i]), new Vector2(250 + i * 185, 380), 115);
            _message = "Roll, click dice to HOLD, then reroll once or bank the total."; LayoutTable(); return;
        }
        _deck = _mode switch
        {
            2 => BuiltInDecks.Tarot(seed: 2718 + _round),
            3 => new Deck(new[] { "fire", "water" }.SelectMany(e => Enumerable.Range(1, 6).Select(i => new CardDefinition(e + i, e + " " + i, $"custom/{e}-{i}", value: i))), "Flame & tide", seed: 2718 + _round),
            _ => BuiltInDecks.PlayingCards(seed: 2718 + _round, theme: _mode == 1 ? "simple" : "traditional")
        };
        _deck.Shuffle();
        var stack = _table.AddArea(_deck.DrawPile, new SKRect(40, 200, 220, 510), CardLayout.Stack); stack.AcceptsDrops = false; stack.CardWidth = 130; stack.CardHeight = 208;
        var hand = _table.AddArea(_hand, new SKRect(260, 200, 1030, 545), CardLayout.Fan); hand.CardWidth = _mode == 2 ? 145 : 130; hand.CardHeight = _mode == 2 ? 243 : 208;
        _table.AddArea(_deck.DiscardPile, new SKRect(1060, 220, 1235, 520), CardLayout.Stack);
        if (_mode == 3)
        {
            _bonusDeck = new Deck(new[] { "earth", "air" }.SelectMany(e => Enumerable.Range(1, 6).Select(i => new CardDefinition(e + i, e + " " + i, $"custom/{e}-{i}", "backs/crimson.svg", i))), "Stone & sky", seed: 321 + _round);
            _bonusDeck.Shuffle();
            var second = _table.AddArea(_bonusDeck.DrawPile, new SKRect(40, 540, 220, 725), CardLayout.Stack); second.CardWidth = 78; second.CardHeight = 125; second.AcceptsDrops = false;
            var bonus = _table.AddArea(_bonus, new SKRect(1060, 550, 1235, 735), CardLayout.Row); bonus.CardWidth = 78; bonus.CardHeight = 125;
        }
        LayoutTable();
        PrepareDecks();
    }
    private void SetupDealer()
    {
        _deck = BuiltInDecks.PlayingCards(seed: 2718 + _round);
        _deck.Shuffle();
        _dealerBoard = _table.AddArea(new CardPile("Free board"), new(20, 170, _tableWidth - 20, 650), CardLayout.Manual);
        _dealerBoard.CardWidth = 88; _dealerBoard.CardHeight = 140;
        var stock = _table.AddArea(_deck.DrawPile, new(CenterX - 70, 240, CenterX + 70, 440), CardLayout.Stack);
        stock.CardWidth = 88; stock.CardHeight = 140; stock.AcceptsDrops = false;
        for (int i = 0; i < 3; i++)
        {
            var player = _table.AddArea(new CardPile($"Player {i + 1}"), new(40, 240, 390, 440), CardLayout.Fan);
            player.CardWidth = 88; player.CardHeight = 140; _players.Add(player);
        }
        LayoutTable();
        PrepareDecks();
    }
    private void PrepareDecks()
    {
        _preparation = _table.BeginPrepareCards(_bonusDeck == null ? _deck.Cards : _deck.Cards.Concat(_bonusDeck.Cards));
    }
    private bool WaitForArtwork(Action action)
    {
        if (_preparation?.Error != null) { _message = "Card artwork could not be loaded. Choose another table or try Reset Table."; return true; }
        if (_preparation is { IsComplete: false }) { _afterPreparation ??= action; return true; }
        return false;
    }
    private void DealPlayers()
    {
        if (WaitForArtwork(DealPlayers)) return;
        if (_table.IsAnimating) return;
        _table.Shuffle(_deck.DrawPile, _deck.Random);
        _table.DealRoundRobin(_deck, _players, 1, faceUp: true);
        _table.DealRoundRobin(_deck, _players, 1, faceUp: false);
        _dealerSequence = _table.DealRoundRobin(_deck, _players, 1, faceUp: true);
    }
    private void DealPoint()
    {
        if (WaitForArtwork(DealPoint)) return;
        if (_table.IsAnimating) return;
        _dealerSequence = _table.DealTo(_deck, _dealerBoard!, new CardPose(new(CenterX + 180, 270), 18),
            animation: new DealAnimation { Style = DealStyle.Toss, Duration = .65 });
    }
    private void DrawDealerHud()
    {
        _hud.Text($"DECK - {_deck.DrawPile.Count} cards", CenterX, 220, SKTypeface.Default, 15, Muted);
        foreach (var area in _players)
            _hud.Text($"{area.Pile.Name.ToUpperInvariant()} - {area.Pile.Cards.Where(c => c.IsFaceUp).Sum(c => c.Definition.Value)} points",
                area.Bounds.MidX, area.Bounds.Top - 15, SKTypeface.Default, 15, Gold);
        _hud.Text(_table.IsDealing ? "Dealing one card at a time: player 1, player 2, player 3..." :
            "Click a hidden card to flip it. Select or drag revealed cards around the table.", CenterX, 665, SKTypeface.Default, 15, Cream);
        Button("deal-players", "DEAL 3 EACH", CenterX - 330, 700, 200, true);
        Button("deal-point", "DEAL TO A POINT", CenterX - 110, 700, 220);
        Button("reset", "CLEAR TABLE", CenterX + 130, 700, 200);
    }
    private void ResizeTable()
    {
        if (_canvas.ActualHeight <= 0 || _canvas.ActualWidth <= 0) return;
        int width = Math.Max(1280, (int)Math.Round(800 * _canvas.ActualWidth / _canvas.ActualHeight));
        if (width == _tableWidth) return;
        _tableWidth = width;
        _table.CancelDrag();
        _canvas.SetRenderResolution(width, 800);
        _drawing.ScreenBounds = new Rectangle(0, 0, width, 800);
        LayoutTable();
    }
    private void LayoutTable()
    {
        if (_mode == 6 && _dealerBoard != null)
        {
            _dealerBoard.Bounds = new(20, 170, _tableWidth - 20, 650);
            _table.Areas.Single(a => ReferenceEquals(a.Pile, _deck.DrawPile)).Bounds = new(CenterX - 70, 240, CenterX + 70, 440);
            _players[0].Bounds = new(40, 250, 390 + ExtraWidth / 3, 450);
            _players[1].Bounds = new(_tableWidth - 390 - ExtraWidth / 3, 250, _tableWidth - 40, 450);
            _players[2].Bounds = new(CenterX - 260, 485, CenterX + 260, 650);
            return;
        }
        foreach (var area in _table.Areas)
        {
            if (ReferenceEquals(area.Pile, _hand)) area.Bounds = new SKRect(260, 200, 1030 + ExtraWidth, 545);
            else if (ReferenceEquals(area.Pile, _deck.DiscardPile)) area.Bounds = new SKRect(1060 + ExtraWidth, 220, 1235 + ExtraWidth, 520);
            else if (ReferenceEquals(area.Pile, _bonus)) area.Bounds = new SKRect(1060 + ExtraWidth, 550, 1235 + ExtraWidth, 735);
        }
        for (int i = 0; i < _table.Dice.Count; i++)
            _table.Dice[i].Center = new Vector2(CenterX + (i - (_table.Dice.Count - 1) / 2f) * Math.Min(220, (_tableWidth - 400f) / _table.Dice.Count), 380);
    }
    private int Target => _mode switch { 2 => 22, 3 => 16, 4 => _diceSet switch { 0 => 20, 1 => 32, 2 => 24, _ => 65 }, _ => 36 };
    private void Act()
    {
        if (_mode < 4 && _stage == 0 && WaitForArtwork(Act)) return;
        if (_mode == 5) { _galleryPage++; return; }
        if (_table.IsAnimating) return;
        if (_stage == 2) { _round++; Setup(); return; }
        if (_mode == 4) { _table.Roll(); if (_stage == 1) { _stage = 2; _message = "Dice settling…"; } else { _stage = 1; _message = "Click dice to HOLD. Reroll once, or bank this roll."; } return; }
        if (_stage == 0)
        {
            _table.Shuffle(_deck.DrawPile, _deck.Random);
            if (_bonusDeck != null) _table.Shuffle(_bonusDeck.DrawPile, _bonusDeck.Random);
            _dealAfterShuffle = true;
            _shuffleStartedAt = Engine.Instance.TotalSecondsEngineRunning;
            _shuffleSnapshot = 0;
            _message = "Shuffling the deck...";
            return;
        }
        int count = _hand.Cards.Count(c => !c.IsSelected);
        foreach (var c in _hand.Cards.Where(c => !c.IsSelected).ToArray()) _deck.Discard(c);
        _table.Deal(_deck, _hand, count); _stage = 2;
    }
    private void Bank() { if (_stage == 1 && !_table.IsAnimating) _stage = 2; }
    private void Frame()
    {
        ResizeTable();
        double now = Engine.Instance.TotalSecondsEngineRunning;
        _table.Update(Math.Max(0, now - _lastFrameTime));
        _lastFrameTime = now;
        if (_afterPreparation != null && _preparation is { IsComplete: true })
        {
            var action = _afterPreparation; _afterPreparation = null;
            if (_preparation.Error == null && !_preparation.IsCanceled) action();
            else _message = "Card artwork could not be loaded. Try Reset Table.";
        }
        if (_dealAfterShuffle && !_table.IsAnimating)
        {
            _dealAfterShuffle = false;
            _table.Deal(_deck, _hand, _mode is 2 or 3 ? 3 : 5);
            if (_bonusDeck != null) _table.Deal(_bonusDeck, _bonus);
            _stage = 1;
            _message = "Choose cards to KEEP, then replace once or bank your score.";
        }
        if (_mode is not (5 or 6))
        {
            _score = _mode == 4 ? (_diceSet == 3 && _table.Dice.All(d => d.Die.Result != null) ? Die.PercentileResult(_table.Dice[0].Die, _table.Dice[1].Die) : _table.Dice.Sum(d => d.Die.Result?.Value ?? 0)) : _hand.Cards.Sum(c => c.Definition.Value) + _bonus.Cards.Sum(c => c.Definition.Value);
            if (_stage == 2 && !_table.IsAnimating) _message = (_score >= Target ? "YOU WIN" : "TRY AGAIN") + $"  ·  {_score} points against a target of {Target}. Start a new round.";
        }
        DrawHud();
        if (Environment.GetEnvironmentVariable("CARDSDICE_WALKTHROUGH") == "1")
        {
            if (_mode == 2 && !_loadingCaptured && _afterPreparation != null && _preparation is { Progress: > .1, IsComplete: false })
            {
                _loadingCaptured = true;
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "cardsdice-loading.png"), _canvas.Host.Backbuffer.ToByteArray());
                Console.WriteLine("CARDSDICE responsive Tarot loading: progress displayed; deal queued.");
            }
            Walkthrough();
        }
    }
    private void Button(string id, string label, float x, float y, float w, bool active = false)
    {
        _hud.Rectangle(x + w / 2, y + 21, w, 42, active ? SKColor.Parse("#b69757") : SKColor.Parse("#183a40"), Gold, 1, 8);
        _hud.Text(label, x + w / 2, y + 21, SKTypeface.Default, 15, active ? SKColor.Parse("#10252c") : Cream);
        _hud.HitRegion(x + w / 2, y + 21, w, 42, id);
    }
    private void DrawHud()
    {
        _hud.Clear();
        _table.Draw(_hud);
        _hud.Rectangle(CenterX, 81.5, _tableWidth, 163, SKColor.Parse("#10252c"));
        _hud.Text("THE FORTUNE TABLE", 42, 48, SKTypeface.Default, 30, Gold, SKTextAlign.Left);
        _hud.Text("CARDS • DICE • A LITTLE CHANCE", 44, 77, SKTypeface.Default, 13, Muted, SKTextAlign.Left);
        for (int i = 0; i < Modes.Length; i++) Button("mode:" + i, Modes[i], 42 + i * ((_tableWidth - 84f) / Modes.Length), 102, (_tableWidth - 84f) / Modes.Length - 15, i == _mode);
        _hud.Text(_mode == 6 ? "DEALING AROUND THE TABLE" : _mode == 5 ? "EMBEDDED SVG COLLECTION" : $"ROUND {_round:00}     TARGET {Target}     SCORE {_score}", 1235 + ExtraWidth, 50, SKTypeface.Default, 19, Cream, SKTextAlign.Right);
        if (_mode == 5) Gallery();
        else if (_mode == 6) DrawDealerHud();
        else
        {
            if (_mode != 4) { _hud.Text(_deck.DrawPile.Name.ToUpperInvariant() + $" · {_deck.DrawPile.Count}", 130, 186, SKTypeface.Default, 13, Muted); _hud.Text("DISCARDS", 1148 + ExtraWidth, 200, SKTypeface.Default, 13, Muted); }
            if (_mode < 4)
            {
                var area = _table.Areas.Single(a => ReferenceEquals(a.Pile, _hand));
                var poses = CardLayouts.Arrange(area.Layout, _hand.Count, area.Bounds.Left, area.Bounds.Top,
                    area.Bounds.Width, area.Bounds.Height, area.CardWidth, area.CardHeight);
                for (int i = 0; i < _hand.Count; i++)
                    _hud.Text($"+{_hand.Cards[i].Definition.Value}", poses[i].Center.X, 560, SKTypeface.Default, 18, Gold);
                if (_bonusDeck != null)
                {
                    _hud.Text("SECOND DECK", 130, 554, SKTypeface.Default, 12, Muted);
                    _hud.Text($"BONUS +{_bonus.Cards.Sum(c => c.Definition.Value)}", 1148 + ExtraWidth, 565, SKTypeface.Default, 12, Gold);
                }
            }
            else
                foreach (var die in _table.Dice)
                    _hud.Text(_diceSet == 3 ? (ReferenceEquals(die, _table.Dice[0]) ? "TENS" : "UNITS") : $"D{die.Die.Faces.Count}",
                        die.Center.X, 505, SKTypeface.Default, 15, Muted);
            _hud.Text(_message, CenterX, 590, SKTypeface.Default, 17, Cream);
            if (_mode == 2) _hud.Text("Historical art · points are sample-game values", CenterX, 622, SKTypeface.Default, 13, Muted);
            _hud.Text("Click to keep / hold · Drag cards between areas · Right-click to inspect", CenterX, 655, SKTypeface.Default, 14, Muted);
            Button("act", _stage == 2 ? "NEW ROUND" : _stage == 0 ? (_mode == 4 ? "ROLL DICE" : "SHUFFLE & DEAL") : (_mode == 4 ? "REROLL" : "REPLACE"), 380 + ExtraWidth / 2, 690, 210, true);
            Button("bank", "BANK SCORE", 610 + ExtraWidth / 2, 690, 165);
            if (_mode == 4) Button("dice", new[] { "5 × D6", "POLYHEDRAL", "5 × D7", "PERCENTILE" }[_diceSet], 800 + ExtraWidth / 2, 690, 185);
            else Button("flip", "FLIP SELECTED", 800 + ExtraWidth / 2, 690, 185);
        }
        _hud.Rectangle(CenterX, 777, _tableWidth, 46, SKColor.Parse("#10252c"));
        Button("sound", _table.SoundEnabled ? "SOUND ON" : "SOUND OFF", 42, 758, 155);
        Button("motion", _table.ReducedMotion ? "MOTION OFF" : "MOTION ON", 213, 758, 165);
        Button("speed", $"SPEED {_table.AnimationSpeed:0.#}×", 394, 758, 155);
        Button("reset", "RESET TABLE", 1060 + ExtraWidth, 758, 175);
        if (_inspect != null)
        {
            _hud.Rectangle(CenterX, 400, _tableWidth, 800, new SKColor(3, 11, 18, 235));
            _hud.Image(_table.Image(_inspect), CenterX, 372.5, 520, 675);
            _hud.Text(_inspect, CenterX, 736, SKTypeface.Default, 17, Gold);
            _hud.Text("Click anywhere to return", CenterX, 772, SKTypeface.Default, 16, Cream);
        }
        if (_afterPreparation != null && _preparation is { IsComplete: false } preparing)
        {
            _hud.Rectangle(CenterX, 590, 580, 100, SKColor.Parse("#10252c"), Gold, 1, 12);
            _hud.Text($"Preparing cards - {preparing.Progress:P0}", CenterX, 571, SKTypeface.Default, 19, Cream);
            _hud.Rectangle(CenterX, 606, 520, 14, SKColor.Parse("#28454b"), cornerRadius: 6);
            float filled = (float)preparing.Progress * 520;
            if (filled > 0) _hud.Rectangle(CenterX - 260 + filled / 2, 606, filled, 14, Gold, cornerRadius: 6);
        }
        _hud.Publish();
    }
    private void Gallery()
    {
        var assets = AssetCatalog.All.Where(a => a.Key.EndsWith(".svg")).ToArray(); int pages = (assets.Length + 23) / 24; _galleryPage %= pages;
        for (int i = 0; i < 24; i++)
        {
            int index = _galleryPage * 24 + i; if (index >= assets.Length) break;
            var a = assets[index]; float x = 45 + ExtraWidth / 16 + (i % 8) * (150 + ExtraWidth / 8), y = 175 + (i / 8) * 166;
            _hud.Rectangle(x + 70, y + 78, 140, 156, new SKColor(232, 220, 193), cornerRadius: 9);
            _hud.Image(_table.Image(a.Key), x + 70, y + 63, 110, 112);
            string name = a.Name.Length > 18 ? a.Name[..17] + "…" : a.Name;
            _hud.Text(name, x + 70, y + 142, SKTypeface.Default, 12, SKColor.Parse("#193139")); _hud.HitRegion(x + 70, y + 78, 140, 156, "asset:" + a.Key);
        }
        Button("prev", "PREVIOUS", 390 + ExtraWidth / 2, 690, 160); Button("act", "NEXT PAGE", 735 + ExtraWidth / 2, 690, 160);
        _hud.Text($"{_galleryPage + 1} / {pages} · {assets.Length} SVGs", CenterX + 5, 718, SKTypeface.Default, 16, Cream);
    }
    private void Mouse(MouseEventArgs e)
    {
        _table.Pointer(new Vector2(e.CurrentPosition.X, e.CurrentPosition.Y), e.LeftButtonJustPressed, e.LeftButtonJustReleased, e.RightButtonJustPressed);
        if (!e.LeftButtonJustPressed) return;
        if (_inspect != null) { _inspect = null; _table.InputEnabled = true; return; }
        var hit = _hud.Published.HitTest(e.CurrentPosition.X, e.CurrentPosition.Y); if (hit == null) return;
        string id = hit.Value.Id;
        if (id.StartsWith("mode:")) { _mode = int.Parse(id[5..]); Setup(); }
        else if (id.StartsWith("asset:")) { _inspect = id[6..]; _table.InputEnabled = false; }
        else switch (id)
            {
                case "deal-players": DealPlayers(); break;
                case "deal-point": DealPoint(); break;
                case "act": Act(); break;
                case "bank": Bank(); break;
                case "prev": _galleryPage = Math.Max(0, _galleryPage - 1); break;
                case "sound": _table.SoundEnabled = !_table.SoundEnabled; break;
                case "motion": _table.ReducedMotion = !_table.ReducedMotion; break;
                case "speed": _table.AnimationSpeed = _table.AnimationSpeed == 1 ? 2 : _table.AnimationSpeed == 2 ? .5f : 1; break;
                case "reset": Setup(); break;
                case "dice": _diceSet = (_diceSet + 1) % 4; Setup(); break;
                case "flip": foreach (var c in _hand.Cards.Where(c => c.IsSelected)) _table.Flip(c); break;
            }
    }
    private void Walkthrough()
    {
        double now = Engine.Instance.TotalSecondsEngineRunning;
        if (_dealAfterShuffle && _shuffleSnapshot < 3 && now - _shuffleStartedAt >= .5 + _shuffleSnapshot * .85)
        {
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), $"cardsdice-shuffle-{_mode}-{_shuffleSnapshot}.png"), _canvas.Host.Backbuffer.ToByteArray());
            _shuffleSnapshot++;
        }
        if (now < _walkAt || _table.IsAnimating || _dealAfterShuffle || _afterPreparation != null) return;
        _walkAt = now + 2.2;
        // Exercise every mode through its actual game commands, not an alternate implementation.
        if (_walkStep >= 30) { Console.WriteLine("CARDSDICE WALKTHROUGH PASS"); Engine.Instance.Stop(); return; }
        int scenario = _walkStep / 3, action = _walkStep % 3;
        int mode = scenario < 5 ? scenario : scenario < 8 ? 4 : scenario == 8 ? 5 : 6;
        if (action == 0)
        {
            _mode = mode; _diceSet = scenario is >= 5 and < 8 ? scenario - 4 : 0;
            var setupTimer = System.Diagnostics.Stopwatch.StartNew();
            Setup();
            Console.WriteLine($"CARDSDICE mode {mode} setup returned in {setupTimer.Elapsed.TotalMilliseconds:F1} ms");
            if (mode == 6) DealPlayers(); else Act();
        }
        else if (action == 1)
        {
            if (mode == 6) { DealPoint(); }
            else if (mode == 4) _table.Pointer(_table.Dice[0].Center, pressed: true);
            else if (mode < 4) _hand.Cards[0].IsSelected = true;
            if (mode != 6) Act();
        }
        else
        {
            if (mode == 6 && (_players.Any(p => p.Pile.Count != 3) || _dealerBoard!.Pile.Count != 1 || _dealerSequence?.IsComplete != true))
                throw new InvalidOperationException("Dealer sequence did not complete.");
            if (mode is not (5 or 6) && _stage != 2) throw new InvalidOperationException("Round did not finish.");
            Console.WriteLine($"CARDSDICE scenario {scenario} PASS score {_score}");
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), $"cardsdice-mode-{scenario}.png"), _canvas.Host.Backbuffer.ToByteArray());
        }
        _walkStep++;
    }
    /// <summary>Releases game-owned resources after the engine is stopped.</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        Engine.Instance.BeforeFrameRender -= Frame; Engine.Instance.Paused -= _table.CancelDrag; Engine.Instance.Disposing -= Dispose;
        Engine.Instance.Input.MouseEventPoller!.MouseEvent -= Mouse;
        _table.Dispose(); _drawing.Dispose(); _scene.Dispose();
    }
}
