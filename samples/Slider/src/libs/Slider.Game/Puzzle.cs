using CodeBrix.Platform.GameEngine;
using CodeBrix.Platform.GameEngine.Audio;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Coordinates;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Physics.Movement.Scripted;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Scenes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;

namespace Slider.Game
{
    /// <summary>
    /// A classic sliding-tile picture puzzle. Slices an image into an N&#215;M grid of sprite tiles with
    /// one empty space; adjacent tiles slide (with sound) into the open space. Ported from the Gondwana
    /// Slider demo; the game logic references only the engine and .NET (no UI-toolkit dependency).
    /// </summary>
    public class Puzzle : IDisposable
    {
        #region private / internal fields

        internal bool _spriteMoving = false;
        internal bool _isShuffling = false;

        private readonly Action<ScriptedMovement> delMoveStart;
        private readonly Action<ScriptedMovement> delMoveStop;

        private int numColumns;
        private int numRows;
        private Size originalSize;
        private Size adjustedSize;
        private Point openSpace;

        private RenderSurfaceHost<BitmapBackbuffer> _renderSurfaceHost;
        private Tilesheet tilesheet;
        private Scene matrixes;

        private AudioResource slideSound;
        private AudioResource tadaSound;

        #endregion private / internal fields

        #region constructors / destructor

        /// <summary>
        /// Initializes a new instance of the <see cref="Puzzle"/> class.
        /// </summary>
        /// <param name="renderSurfaceHost">The engine render-surface host to render into.</param>
        /// <param name="imgFile">The full path of the image to slice into puzzle tiles.</param>
        /// <param name="columns">The number of puzzle columns.</param>
        /// <param name="rows">The number of puzzle rows.</param>
        /// <param name="size">The size of the render surface, in pixels.</param>
        public Puzzle(RenderSurfaceHost<BitmapBackbuffer> renderSurfaceHost, string imgFile, int columns, int rows, Size size)
        {
            tilesheet = TilesheetRegistry.Instance.LoadFromImageFile("picture", imgFile);
            tilesheet.ApplyPremultiplyAlpha();

            int tileWidth = (int)((float)tilesheet.SkBitmap.Width / (float)columns);
            int tileHeight = (int)((float)tilesheet.SkBitmap.Height / (float)rows);
            int adjWidth = tileWidth * columns;
            int adjHeight = tileHeight * rows;

            tilesheet.DefaultRegion.TileSize = new Size(tileWidth, tileHeight);

            originalSize = new Size(tilesheet.SkBitmap.Width, tilesheet.SkBitmap.Height);
            numColumns = columns;
            numRows = rows;
            adjustedSize = new Size(adjWidth, adjHeight);

            matrixes = new Scene();
            matrixes.AddLayer(numColumns, numRows, tileWidth, tileHeight, 0, 1, CoordinateSystemTypes.Orthogonal);

            Engine.Instance.InitializationComplete += OnEngineInitializationComplete;

            _renderSurfaceHost = renderSurfaceHost;
            _renderSurfaceHost.RedrawDirtyRectangleOnly = true;
            _renderSurfaceHost.Backbuffer.ClearColor = SkiaSharp.SKColors.Black;
            _renderSurfaceHost.Bind(matrixes);

            delMoveStart = Sprites_SpriteMovementStarted;
            delMoveStop = Sprites_SpriteMovementStopped;

            InitializeSprites(tileWidth, tileHeight);
            slideSound = AudioResourceManager.Instance.LoadFromFile("move", AssetPath("75143__willc2-45220__slide-cup-16b-44k-0-747s.wav"));
            tadaSound = AudioResourceManager.Instance.LoadFromFile("tada", AssetPath("177120__rdholder__2dogsound-tadaa1-3s-2013jan31-cc-by-30-us.wav"));
        }

        private static string AssetPath(string fileName)
            => Path.Combine(AppContext.BaseDirectory, "assets", fileName);

        private void OnEngineInitializationComplete()
        {
            Engine.Instance.Configuration.TargetFPS = 120;
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="Puzzle"/> class.
        /// </summary>
        ~Puzzle()
        {
            Dispose();
        }

        #endregion constructors / destructor

        #region public properties

        /// <summary>Gets the number of puzzle columns.</summary>
        public int Columns
        {
            get { return numColumns; }
        }

        /// <summary>Gets the number of puzzle rows.</summary>
        public int Rows
        {
            get { return numRows; }
        }

        /// <summary>Gets the original (unadjusted) size of the source bitmap, in pixels.</summary>
        public Size OriginalBitmapSize
        {
            get { return originalSize; }
        }

        /// <summary>Gets the adjusted bitmap size (evenly divisible by the grid), in pixels.</summary>
        public Size AdjustedBitmapSize
        {
            get { return adjustedSize; }
        }

        /// <summary>Gets the current open (empty) grid space.</summary>
        public Point OpenSpace
        {
            get { return openSpace; }
        }

        /// <summary>Gets a value indicating whether a shuffle sequence is currently in progress.</summary>
        public bool IsShuffling
        {
            get { return _isShuffling; }
        }

        /// <summary>Gets a value indicating whether a piece is currently sliding.</summary>
        public bool IsSpriteMoving
        {
            get { return _spriteMoving; }
        }

        /// <summary>Gets or sets a value indicating whether grid lines are shown.</summary>
        public bool ShowGridLines
        {
            get { return matrixes[0].ShowGridLines; }
            set { matrixes[0].ShowGridLines = value; }
        }

        /// <summary>Gets the total number of puzzle pieces.</summary>
        public int TotalPieces
        {
            get { return SpriteManager.Instance.AllSprites.Count; }
        }

        /// <summary>Gets the number of puzzle pieces currently in their correct position.</summary>
        public int TotalPiecesCorrect
        {
            get
            {
                int totalCorrect = 0;

                foreach (Sprite sprite in SpriteManager.Instance.AllSprites)
                {
                    Point spriteLoc = new Point((int)sprite.SceneLayerCoordinates.X, (int)sprite.SceneLayerCoordinates.Y);

                    if (spriteLoc == ParseSpriteCoordID(sprite.Nickname))
                        totalCorrect++;
                }

                return totalCorrect;
            }
        }

        #endregion public properties

        #region public methods

        /// <summary>
        /// Slides the specified sprite into the open space, if it is adjacent.
        /// </summary>
        /// <param name="sprite">The sprite to slide.</param>
        /// <param name="slideTime">The slide duration, in seconds.</param>
        /// <returns><see langword="true"/> if the sprite moved; otherwise <see langword="false"/>.</returns>
        public bool SlidePiece(Sprite sprite, float slideTime)
        {
            if (FindSpritesAdjToOpenSpace().IndexOf(sprite) == -1)
                // sprite not eligible to move
                return false;
            else
            {
                // capture the starting point of the sprite being moved
                Point startPt = new Point((int)sprite.SceneLayerCoordinates.X, (int)sprite.SceneLayerCoordinates.Y);

                // move the sprite to the open space
                sprite.Movement.MoveTo(new Vector2(openSpace.X, openSpace.Y), slideTime, null, 0.01f);

                // make the openSpace value equal to the original sprite starting point
                openSpace = startPt;

                // move was successful
                return true;
            }
        }

        private int _totalMoves;
        private float _slideTime;
        private int _moveNumber;
        private Sprite _lastMoved;

        /// <summary>
        /// Shuffles the puzzle by performing a sequence of random legal slides.
        /// </summary>
        /// <param name="totalMoves">The number of shuffle moves to perform.</param>
        /// <param name="slideTime">The slide duration for each move, in seconds.</param>
        public void Shuffle(int totalMoves, float slideTime)
        {
            _isShuffling = true;
            _totalMoves = totalMoves;
            _slideTime = slideTime;
            _moveNumber = 0;
            _lastMoved = null;

            ShuffleNext();
        }

        private void ShuffleNext()
        {
            Random random = new Random();

            // find all pieces next to open space
            List<Sprite> sprites = FindSpritesAdjToOpenSpace();

            // pick one of the pieces at random
            Sprite sprite = sprites[random.Next(0, sprites.Count)];

            // don't move the same sprite 2 times in a row
            while (sprite == _lastMoved)
                sprite = sprites[random.Next(0, sprites.Count)];

            // move the piece
            SlidePiece(sprite, _slideTime);
            _lastMoved = sprite;

            if (++_moveNumber >= _totalMoves)
                _isShuffling = false;
        }

        /// <summary>
        /// Converts a surface pixel coordinate to a puzzle grid coordinate.
        /// </summary>
        /// <param name="pxlX">The X pixel coordinate.</param>
        /// <param name="pxlY">The Y pixel coordinate.</param>
        /// <returns>The corresponding grid coordinate.</returns>
        public PointF GetGridCoordinates(int pxlX, int pxlY)
        {
            var view = _renderSurfaceHost.ViewManager.Views[0];
            var worldPx = view.ScreenPxToWorldPx(matrixes[0], new PointF(pxlX, pxlY));
            return matrixes[0].WorldPxToGrid(worldPx);
        }

        #endregion public methods

        #region private methods

        private void InitializeSprites(int tileWidth, int tileHeight)
        {
            SpriteManager.Instance.Clear();

            for (int x = 0; x < numColumns; x++)
            {
                for (int y = 0; y < numRows; y++)
                {
                    Sprite sprite = SpriteManager.Instance.CreateSprite(matrixes[0], new Frame(tilesheet, x, y),
                        x.ToString() + "-" + y.ToString());
                    sprite.SetPosition(new System.Numerics.Vector2((float)x, (float)y));
                    sprite.Visible = true;

                    sprite.Movement.ScriptedMovementStarted += delMoveStart;
                    sprite.Movement.ScriptedMovementStopped += delMoveStop;
                }
            }

            // remove the bottom-right tile; this will be the space for sliding
            int maxX = numColumns - 1;
            int maxY = numRows - 1;
            SpriteManager.Instance.GetSpriteByID(maxX.ToString() + "-" + maxY.ToString()).Dispose();
            openSpace = new Point(maxX, maxY);
        }

        private Point ParseSpriteCoordID(string ID)
        {
            string[] coords = ID.Split('-');
            int x = int.Parse(coords[0]);
            int y = int.Parse(coords[1]);
            return new Point(x, y);
        }

        private List<Sprite> FindSpritesAdjToOpenSpace()
        {
            List<Sprite> adjSprites = new List<Sprite>();
            List<SceneLayerTile> adjGridPts = new List<SceneLayerTile>();

            var layer = matrixes[0];
            var centerTile = layer[openSpace];

            adjGridPts.Add(layer.GetAdjacentTile(centerTile, CardinalDirections.N));
            adjGridPts.Add(layer.GetAdjacentTile(centerTile, CardinalDirections.S));
            adjGridPts.Add(layer.GetAdjacentTile(centerTile, CardinalDirections.E));
            adjGridPts.Add(layer.GetAdjacentTile(centerTile, CardinalDirections.W));

            foreach (SceneLayerTile gPt in adjGridPts)
            {
                if (gPt != null)
                    adjSprites.AddRange(SpriteManager.Instance.GetSpritesInWorldRectRange(gPt.DrawLocationWorld));
            }

            return adjSprites;
        }

        #endregion private methods

        #region event handlers

        private void Sprites_SpriteMovementStarted(ScriptedMovement scriptedMovement)
        {
            _spriteMoving = true;
            slideSound.Play();
        }

        private void Sprites_SpriteMovementStopped(ScriptedMovement scriptedMovement)
        {
            _spriteMoving = false;
            slideSound.Stop();

            if (_isShuffling)
                ShuffleNext();
        }

        #endregion event handlers

        #region IDisposable Members

        /// <summary>
        /// Releases all resources used by the <see cref="Puzzle"/>.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            tilesheet.Dispose();
            matrixes.Dispose();
            SpriteManager.Instance.Clear();
        }

        #endregion IDisposable Members
    }
}
