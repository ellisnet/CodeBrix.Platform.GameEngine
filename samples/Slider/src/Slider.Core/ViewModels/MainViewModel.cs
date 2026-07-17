using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Host;
using CodeBrix.Platform.GameEngine.Host.Rendering;
using CodeBrix.Platform.GameEngine.Input.Mouse;
using CodeBrix.Platform.Simple;
using Slider.Game;

namespace Slider.ViewModels;

public interface IManageGameCanvas
{
    void CanvasFirstStart(GameSurfaceCanvas canvas);
}

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IManageGameCanvas
{
    private GameSurfaceCanvas _canvas;
    private Puzzle _puzzle;
    private bool _engineStarted;

    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");
    }

    #region | Bindable properties |

    private string _columnsText = "4";
    public string ColumnsText
    {
        get => _columnsText;
        set => SetProperty(ref _columnsText, value);
    }

    private string _rowsText = "4";
    public string RowsText
    {
        get => _rowsText;
        set => SetProperty(ref _rowsText, value);
    }

    private bool _showGridLines;
    public bool ShowGridLines
    {
        get => _showGridLines;
        set
        {
            SetProperty(ref _showGridLines, value);
            if (_puzzle is not null)
                _puzzle.ShowGridLines = value;
        }
    }

    private string _coordinateText = "x: -   y: -";
    public string CoordinateText
    {
        get => _coordinateText;
        private set => SetProperty(ref _coordinateText, value, notifyOnMainThread: true);
    }

    private string _infoText = "";
    public string InfoText
    {
        get => _infoText;
        private set => SetProperty(ref _infoText, value, notifyOnMainThread: true);
    }

    #endregion

    #region | Commands and their implementations |

    private SimpleCommand _newPuzzleCommand;
    public SimpleCommand NewPuzzleCommand => (_newPuzzleCommand ??= new SimpleCommand(DoNewPuzzle));

    private void DoNewPuzzle(object parameter)
    {
        if (_puzzle is { IsSpriteMoving: true })
            return;

        BuildPuzzle(ParseDimension(ColumnsText), ParseDimension(RowsText));
    }

    private SimpleCommand _shuffleCommand;
    public SimpleCommand ShuffleCommand => (_shuffleCommand ??= new SimpleCommand(DoShuffle));

    private void DoShuffle(object parameter)
    {
        if (_puzzle is { IsShuffling: false } puzzle)
        {
            int numberOfSlides = puzzle.Rows * puzzle.Columns * 3;
            float slideTime = 15f / numberOfSlides;
            puzzle.Shuffle(numberOfSlides, slideTime);
        }
    }

    #endregion

    #region | IManageGameCanvas implementation |

    public void CanvasFirstStart(GameSurfaceCanvas canvas)
    {
        _canvas = canvas;
        BuildPuzzle(4, 4);
    }

    #endregion

    #region | private methods |

    private void BuildPuzzle(int columns, int rows)
    {
        // ActualWidth/Height are UI-thread properties; capture them before any marshaling.
        var size = new Size((int)_canvas.ActualWidth, (int)_canvas.ActualHeight);

        if (!_engineStarted)
        {
            // First build: the engine loop is not running yet, so there is nothing to race with.
            // Build directly, then start the engine and wire up input.
            RebuildPuzzle(columns, rows, size);

            _engineStarted = true;

            Engine.Instance.Configuration.TargetFPS = 120;
            Engine.Instance.Start(SynchronizationContext.Current);
            Engine.Instance.CPSCalculated += OnCpsCalculated;

            Engine.Instance.InitializeCodeBrixMouseAdapter(_canvas);
            Engine.Instance.Input.MouseEventPoller.MouseEvent += OnMouseEvent;
            return;
        }

        // The engine loop runs on its own thread and enumerates the scene/sprite collections every
        // cycle. Perform the rebuild on that thread (via the engine dispatcher, drained at the start of
        // each cycle before update/render) so disposing the old scene, binding the new one, and
        // clearing/creating sprites are all serialized with the loop instead of racing it. This closes
        // both the transient scene-less-host window and the "collection modified" errors that a
        // UI-thread rebuild causes while the loop is live.
        Engine.Instance.EngineDispatcher.Post(() => RebuildPuzzle(columns, rows, size));
    }

    private void RebuildPuzzle(int columns, int rows, Size size)
    {
        _puzzle?.Dispose();
        _puzzle = new Puzzle(_canvas.Host, PuzzleImagePath(), columns, rows, size);
        _puzzle.ShowGridLines = _showGridLines;
    }

    private static string PuzzleImagePath()
        => Path.Combine(AppContext.BaseDirectory, "assets", "puzzle.bmp");

    private static int ParseDimension(string text)
    {
        if (int.TryParse(text, out var value) && value >= 3 && value <= 20)
            return value;
        return 4;
    }

    private void OnMouseEvent(MouseEventArgs e)
    {
        var puzzle = _puzzle;
        if (puzzle is null)
            return;

        var coords = puzzle.GetGridCoordinates(e.CurrentPosition.X, e.CurrentPosition.Y);
        CoordinateText = $"x: {coords.X}   y: {coords.Y}";

        if (e.ButtonStates.First(s => s.Key == MouseButton.Left).Value.JustPressed)
        {
            if (!puzzle.IsShuffling && !SpriteManager.Instance.AllSprites.Any(s => s.Movement.MovementState.HasMotion))
            {
                List<Sprite> sprites = SpriteManager.Instance.GetSpritesAtViewPixel(
                    _canvas.Host.ViewManager.Views[0],
                    new Point(e.CurrentPosition.X, e.CurrentPosition.Y));

                if (sprites.Count != 0)
                    puzzle.SlidePiece(sprites[0], 0.15f);
            }
        }
    }

    private void OnCpsCalculated(CyclesPerSecondCalculatedEventArgs e)
    {
        InfoText = $"FPS: {e.NetCPS:N2}\nCPS: {e.GrossCPS:N2}\nSample: {e.SampleTime:N2}";
    }

    #endregion
}
