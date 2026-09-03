using System.Drawing;
using SkiaSharp;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing.Tilesheets; //was previously: Gondwana.Drawing.Tilesheets;
/// <summary>
/// Represents a rectangular region within a tilesheet that contains a grid of tiles.
/// </summary>
public sealed class TilesheetRegion : IDisposable
{
    /// <summary>
    /// The default name assigned to tilesheet regions when no name is specified.
    /// </summary>
    public static readonly string DefaultRegionName = "default";

    private TilesheetRegionSlice?[,]? _tileCache;
    private readonly Dictionary<(int x, int y), CollisionAdjust> _frameCollisionAdjustments = new();
    private readonly Dictionary<(int x, int y), TileCollisionType> _frameCollisionTypes = new();
    private CollisionAdjust _collisionAdjust = CollisionAdjust.None;
    private TileCollisionType _collisionType = TileCollisionType.None;
    private bool _disposed;

    #region ctors

    private TilesheetRegion() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="TilesheetRegion"/> class with the specified parameters.
    /// </summary>
    /// <param name="tilesheet">The parent tilesheet that owns this region.</param>
    /// <param name="name">The name of the region. If null or whitespace, the default name is used.</param>
    /// <param name="area">The rectangular area that this region occupies within the tilesheet.</param>
    /// <param name="tileSize">The size of each individual tile in this region.</param>
    /// <param name="tilePadding">The spacing (padding) around each tile within this region.</param>
    /// <param name="regionMargin">The margin spacing around the entire region.</param>
    /// <param name="overhangPixels">The overhang dimensions in pixels that extend beyond a tile's primary area.</param>
    /// <param name="collisionAdjust">The collision adjustment inherited by every frame that has no explicit override.</param>
    /// <param name="collisionType">The collision type inherited by every frame that has no explicit override.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tilesheet"/> is null.</exception>
    internal TilesheetRegion(
        Tilesheet tilesheet,
        string name,
        Rectangle area,
        Size tileSize,
        Spacing tilePadding,
        Spacing regionMargin,
        Spacing overhangPixels,
        CollisionAdjust collisionAdjust,
        TileCollisionType collisionType = TileCollisionType.None)
    {
        Tilesheet = tilesheet ?? throw new ArgumentNullException(nameof(tilesheet));

        Name = string.IsNullOrWhiteSpace(name)
            ? DefaultRegionName
            : name;

        // Assign backing fields directly so we do not rebuild the cache
        // repeatedly during construction.
        _area = area;
        _tileSize = tileSize;
        _tilePadding = tilePadding;
        _regionMargin = regionMargin;
        _collisionAdjust = collisionAdjust;
        _collisionType = collisionType;

        Overhang = overhangPixels;

        BuildTileCache();
    }

    #endregion ctors

    #region serialized fields

    private Rectangle _area;

    private Size _tileSize;

    private Spacing _tilePadding = Spacing.None;

    private Spacing _regionMargin = Spacing.None;

    #endregion serialized fields

    #region properties

    /// <summary>
    /// Gets the tilesheet that owns this region.
    /// </summary>
    public Tilesheet Tilesheet { get; private set; } = null!;

    /// <summary>
    /// Gets the name of this tilesheet region.
    /// </summary>
    public string Name { get; private set; } = DefaultRegionName;

    /// <summary>
    /// Gets or sets the rectangular area that this region occupies within the tilesheet.
    /// Setting this property rebuilds the internal tile cache.
    /// </summary>
    public Rectangle Area
    {
        get => _area;
        set
        {
            _area = value;
            BuildTileCache();
        }
    }

    /// <summary>
    /// Gets or sets the size of each individual tile in this region.
    /// Setting this property rebuilds the internal tile cache.
    /// <para />
    /// The tile size defines the source pixel dimensions of each tile's primary area, excluding any padding
    /// </summary>
    public Size TileSize
    {
        get => _tileSize;
        set
        {
            _tileSize = value;
            BuildTileCache();
        }
    }

    /// <summary>
    /// Gets or sets the spacing (padding) around each tile within this region.
    /// Setting this property rebuilds the internal tile cache.
    /// </summary>
    public Spacing TilePadding
    {
        get => _tilePadding;
        set
        {
            _tilePadding = value;
            BuildTileCache();
        }
    }

    /// <summary>
    /// Gets or sets the margin spacing around the entire region.
    /// Setting this property rebuilds the internal tile cache.
    /// </summary>
    public Spacing RegionMargin
    {
        get => _regionMargin;
        set
        {
            _regionMargin = value;
            BuildTileCache();
        }
    }

    /// <summary>
    /// Represents the overhang dimensions in pixels that extend beyond a tile's primary area.
    /// Overhang values define how much a tile's visual representation exceeds its logical boundaries
    /// in each direction (left, top, right, and bottom).
    /// <para />
    /// This property only affects how the tile is rendered; it does not affect how the tile is sliced
    /// and cached.
    /// </summary>
    public Spacing Overhang { get; set; } = Spacing.None;

    /// <summary>
    /// Gets or sets the collision adjustment inherited by every frame in this region that does
    /// not carry an explicit frame-level override.
    /// </summary>
    /// <remarks>
    /// Assigning this property re-applies the new default to inheriting frames only; frames with
    /// an explicit override (see <see cref="SetFrameCollisionAdjust"/>) keep their own value until
    /// it is removed with <see cref="ClearFrameCollisionAdjustOverride"/>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public CollisionAdjust CollisionAdjust
    {
        get => _collisionAdjust;
        set
        {
            ThrowIfDisposed();

            _collisionAdjust = value;

            ApplyDefaultCollisionAdjustToCache();
        }
    }

    /// <summary>
    /// Gets or sets the collision type inherited by every frame in this region that does not carry
    /// an explicit frame-level override.
    /// </summary>
    /// <remarks>
    /// Assigning this property changes the effective type of inheriting frames only; frames with an
    /// explicit override (see <see cref="SetFrameCollisionType"/>) keep their own value until it is
    /// removed with <see cref="ClearFrameCollisionTypeOverride"/>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public TileCollisionType CollisionType
    {
        get => _collisionType;
        set
        {
            ThrowIfDisposed();

            _collisionType = value;
        }
    }

    /// <summary>
    /// Gets the region-default frame-local collision rectangle, derived from <see cref="TileSize"/>
    /// and <see cref="CollisionAdjust"/>.
    /// </summary>
    public Rectangle CollisionArea =>
        _collisionAdjust.ApplyTo(new Rectangle(Point.Empty, _tileSize));

    /// <summary>
    /// Gets the number of columns (horizontal tiles) in this region.
    /// </summary>
    public int Columns => _tileCache?.GetLength(0) ?? 0;

    /// <summary>
    /// Gets the number of rows (vertical tiles) in this region.
    /// </summary>
    public int Rows => _tileCache?.GetLength(1) ?? 0;

    /// <summary>
    /// Gets the total width of a single tile including its padding.
    /// </summary>
    public int TileWidthIncludingPadding => _tilePadding.Left + _tileSize.Width + _tilePadding.Right;

    /// <summary>
    /// Gets the total height of a single tile including its padding.
    /// </summary>
    public int TileHeightIncludingPadding => _tilePadding.Top + _tileSize.Height + _tilePadding.Bottom;

    #endregion properties

    #region public methods

    /// <summary>
    /// Gets the image for the tile at the specified grid coordinates.
    /// </summary>
    /// <param name="x">The column index of the tile.</param>
    /// <param name="y">The row index of the tile.</param>
    /// <returns>The SKImage for the tile, or null if the coordinates are out of bounds or the tile cache is invalid.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public SKImage? GetImage(int x, int y)
    {
        ThrowIfDisposed();

        if (_tileCache == null)
            BuildTileCache();

        if (_tileCache == null)
            return null;

        if ((uint)x >= (uint)_tileCache.GetLength(0) ||
            (uint)y >= (uint)_tileCache.GetLength(1))
            return null;

        return _tileCache[x, y]?.Image;
    }

    /// <summary>
    /// Gets the bitmap for the tile at the specified grid coordinates.
    /// </summary>
    /// <param name="x">The column index of the tile.</param>
    /// <param name="y">The row index of the tile.</param>
    /// <returns>The SKBitmap for the tile, or null if the coordinates are out of bounds or the tile cache is invalid.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public SKBitmap? GetBitmap(int x, int y)
    {
        ThrowIfDisposed();

        if (_tileCache == null)
            BuildTileCache();

        if (_tileCache == null)
            return null;

        if ((uint)x >= (uint)_tileCache.GetLength(0) ||
            (uint)y >= (uint)_tileCache.GetLength(1))
            return null;

        return _tileCache[x, y]?.Bitmap;
    }

    /// <summary>
    /// Gets all bitmaps in this region as a dictionary keyed by their grid coordinates.
    /// </summary>
    /// <returns>A dictionary mapping (x, y) coordinates to their corresponding SKBitmap instances.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public Dictionary<(int x, int y), SKBitmap> GetAllBitmaps()
    {
        ThrowIfDisposed();

        if (_tileCache == null)
            BuildTileCache();

        var bitmaps = new Dictionary<(int x, int y), SKBitmap>();

        if (_tileCache == null)
            return bitmaps;

        for (int y = 0; y < _tileCache.GetLength(1); y++)
        {
            for (int x = 0; x < _tileCache.GetLength(0); x++)
            {
                var slice = _tileCache[x, y];

                if (slice.HasValue)
                    bitmaps[(x, y)] = slice.Value.Bitmap;
            }
        }

        return bitmaps;
    }

    /// <summary>
    /// Gets all images in this region as a dictionary keyed by their grid coordinates.
    /// </summary>
    /// <returns>A dictionary mapping (x, y) coordinates to their corresponding SKImage instances.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public Dictionary<(int x, int y), SKImage> GetAllImages()
    {
        ThrowIfDisposed();

        if (_tileCache == null)
            BuildTileCache();

        var images = new Dictionary<(int x, int y), SKImage>();

        if (_tileCache == null)
            return images;

        for (int y = 0; y < _tileCache.GetLength(1); y++)
        {
            for (int x = 0; x < _tileCache.GetLength(0); x++)
            {
                var slice = _tileCache[x, y];

                if (slice.HasValue)
                    images[(x, y)] = slice.Value.Image;
            }
        }

        return images;
    }

    /// <summary>
    /// Gets the effective collision adjustment for the frame at the specified grid coordinates.
    /// </summary>
    /// <param name="x">The column index of the frame.</param>
    /// <param name="y">The row index of the frame.</param>
    /// <returns>
    /// The frame's explicit override when one exists; otherwise the region's
    /// <see cref="CollisionAdjust"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public CollisionAdjust GetFrameCollisionAdjust(int x, int y)
    {
        ThrowIfDisposed();

        if (!IsFrameCoordinateValid(x, y))
            return _collisionAdjust;

        var slice = _tileCache![x, y];

        if (slice.HasValue)
            return slice.Value.CollisionAdjust;

        return GetStoredFrameCollisionAdjust(x, y);
    }

    /// <summary>
    /// Attempts to get the explicit collision adjustment assigned to the frame at the specified
    /// grid coordinates.
    /// </summary>
    /// <param name="x">The column index of the frame.</param>
    /// <param name="y">The row index of the frame.</param>
    /// <param name="collisionAdjust">
    /// When this method returns <see langword="true"/>, the frame's explicit override; otherwise
    /// an unspecified value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an explicit frame-level override exists; <see langword="false"/>
    /// when the frame inherits <see cref="CollisionAdjust"/> or the coordinates are out of range.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public bool TryGetFrameCollisionAdjustOverride(int x, int y, out CollisionAdjust collisionAdjust)
    {
        ThrowIfDisposed();

        if (_tileCache == null)
            BuildTileCache();

        if (!IsFrameCoordinateValid(x, y))
        {
            collisionAdjust = default;
            return false;
        }

        return _frameCollisionAdjustments.TryGetValue((x, y), out collisionAdjust);
    }

    /// <summary>
    /// Records an explicit collision adjustment for the frame at the specified grid coordinates
    /// and updates its cache entry.
    /// </summary>
    /// <param name="x">The column index of the frame.</param>
    /// <param name="y">The row index of the frame.</param>
    /// <param name="collisionAdjust">The collision adjustment to record for the frame.</param>
    /// <remarks>
    /// The frame is marked as explicitly overridden even when <paramref name="collisionAdjust"/>
    /// currently equals the region default, so a later change to <see cref="CollisionAdjust"/>
    /// leaves it untouched.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the coordinates fall outside the region.</exception>
    public void SetFrameCollisionAdjust(int x, int y, CollisionAdjust collisionAdjust)
    {
        ThrowIfDisposed();

        if (_tileCache == null)
            BuildTileCache();

        if (!IsFrameCoordinateValid(x, y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Frame coordinates ({x}, {y}) are outside region '{Name}'.");
        }

        _frameCollisionAdjustments[(x, y)] = collisionAdjust;

        var slice = _tileCache![x, y];

        if (slice.HasValue)
            _tileCache[x, y] = slice.Value.WithCollisionAdjust(collisionAdjust);
    }

    /// <summary>
    /// Removes the explicit collision adjustment from the frame at the specified grid coordinates
    /// so that it once again inherits <see cref="CollisionAdjust"/>.
    /// </summary>
    /// <param name="x">The column index of the frame.</param>
    /// <param name="y">The row index of the frame.</param>
    /// <returns>
    /// <see langword="true"/> when an explicit frame override was removed; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the coordinates fall outside the region.</exception>
    public bool ClearFrameCollisionAdjustOverride(int x, int y)
    {
        ThrowIfDisposed();

        if (_tileCache == null)
            BuildTileCache();

        if (!IsFrameCoordinateValid(x, y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Frame coordinates ({x}, {y}) are outside region '{Name}'.");
        }

        if (!_frameCollisionAdjustments.Remove((x, y)))
            return false;

        var slice = _tileCache![x, y];

        if (slice.HasValue)
            _tileCache[x, y] = slice.Value.WithCollisionAdjust(_collisionAdjust);

        return true;
    }

    /// <summary>
    /// Gets the frame-local collision rectangle for the frame at the specified grid coordinates.
    /// </summary>
    /// <param name="x">The column index of the frame.</param>
    /// <param name="y">The row index of the frame.</param>
    /// <returns>The collision rectangle relative to the frame's top-left corner.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public Rectangle GetFrameCollisionArea(int x, int y) =>
        GetFrameCollisionAdjust(x, y).ApplyTo(new Rectangle(Point.Empty, _tileSize));

    /// <summary>
    /// Gets the effective collision type for the frame at the specified grid coordinates.
    /// </summary>
    /// <param name="x">The column index of the frame.</param>
    /// <param name="y">The row index of the frame.</param>
    /// <returns>
    /// The frame's explicit override when one exists; otherwise the region's <see cref="CollisionType"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public TileCollisionType GetFrameCollisionType(int x, int y)
    {
        ThrowIfDisposed();

        if (!IsFrameCoordinateValid(x, y))
            return _collisionType;

        return _frameCollisionTypes.TryGetValue((x, y), out var collisionType)
            ? collisionType
            : _collisionType;
    }

    /// <summary>
    /// Attempts to get the explicit collision type assigned to the frame at the specified grid
    /// coordinates.
    /// </summary>
    /// <param name="x">The column index of the frame.</param>
    /// <param name="y">The row index of the frame.</param>
    /// <param name="collisionType">
    /// When this method returns <see langword="true"/>, the frame's explicit override; otherwise
    /// an unspecified value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an explicit frame-level override exists; <see langword="false"/>
    /// when the frame inherits <see cref="CollisionType"/> or the coordinates are out of range.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public bool TryGetFrameCollisionTypeOverride(int x, int y, out TileCollisionType collisionType)
    {
        ThrowIfDisposed();

        if (_tileCache == null)
            BuildTileCache();

        if (!IsFrameCoordinateValid(x, y))
        {
            collisionType = default;
            return false;
        }

        return _frameCollisionTypes.TryGetValue((x, y), out collisionType);
    }

    /// <summary>
    /// Records an explicit collision type for the frame at the specified grid coordinates.
    /// </summary>
    /// <param name="x">The column index of the frame.</param>
    /// <param name="y">The row index of the frame.</param>
    /// <param name="collisionType">The collision type to record for the frame.</param>
    /// <remarks>
    /// The frame is marked as explicitly overridden even when <paramref name="collisionType"/>
    /// currently equals the region default, so a later change to <see cref="CollisionType"/>
    /// leaves it untouched.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the coordinates fall outside the region.</exception>
    public void SetFrameCollisionType(int x, int y, TileCollisionType collisionType)
    {
        ThrowIfDisposed();

        if (_tileCache == null)
            BuildTileCache();

        if (!IsFrameCoordinateValid(x, y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Frame coordinates ({x}, {y}) are outside region '{Name}'.");
        }

        _frameCollisionTypes[(x, y)] = collisionType;
    }

    /// <summary>
    /// Removes the explicit collision type from the frame at the specified grid coordinates so that
    /// it once again inherits <see cref="CollisionType"/>.
    /// </summary>
    /// <param name="x">The column index of the frame.</param>
    /// <param name="y">The row index of the frame.</param>
    /// <returns>
    /// <see langword="true"/> when an explicit frame override was removed; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the coordinates fall outside the region.</exception>
    public bool ClearFrameCollisionTypeOverride(int x, int y)
    {
        ThrowIfDisposed();

        if (_tileCache == null)
            BuildTileCache();

        if (!IsFrameCoordinateValid(x, y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Frame coordinates ({x}, {y}) are outside region '{Name}'.");
        }

        return _frameCollisionTypes.Remove((x, y));
    }

    #endregion public methods

    #region internal methods

    /// <summary>
    /// Builds the internal tile cache by slicing the tilesheet bitmap into individual tiles based on the region's configuration.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when tile padding or region margin values are negative.</exception>
    internal void BuildTileCache()
    {
        ThrowIfDisposed();
        ClearTileCache();

        if (Tilesheet == null)
            return;

        if (Tilesheet.SkBitmap == null || Tilesheet.SkBitmap.IsEmpty)
            return;

        if (_tileSize.Width <= 0 || _tileSize.Height <= 0)
            return;

        if (_area.Width <= 0 || _area.Height <= 0)
            return;

        if (_tilePadding.Left < 0 || _tilePadding.Top < 0 || _tilePadding.Right < 0 || _tilePadding.Bottom < 0)
            throw new InvalidOperationException("Tilesheet region tile padding cannot be negative.");

        if (_regionMargin.Left < 0 || _regionMargin.Top < 0 || _regionMargin.Right < 0 || _regionMargin.Bottom < 0)
            throw new InvalidOperationException("Tilesheet region margin cannot be negative.");

        if (TileWidthIncludingPadding <= 0 || TileHeightIncludingPadding <= 0)
            return;

        int xTiles = (_area.Width - _regionMargin.Left - _regionMargin.Right) / TileWidthIncludingPadding;
        int yTiles = (_area.Height - _regionMargin.Top - _regionMargin.Bottom) / TileHeightIncludingPadding;

        if (xTiles <= 0 || yTiles <= 0)
            return;

        PruneFrameCollisionAdjustments(xTiles, yTiles);
        PruneFrameCollisionTypes(xTiles, yTiles);

        _tileCache = new TilesheetRegionSlice?[xTiles, yTiles];

        var regionArea = Area;
        var bitmapBounds = Tilesheet.SkBitmap.Info.Rect;

        for (int y = 0; y < yTiles; y++)
        {
            for (int x = 0; x < xTiles; x++)
            {
                var srcRect = GetTileBounds(x, y);

                // Prevent this region from bleeding into another differently-sized row/section.
                if (!regionArea.Contains(srcRect))
                    continue;

                // Prevent invalid reads outside the source image.
                if (!bitmapBounds.Contains(srcRect.ToSKRectI()))
                    continue;

                var slice = CreateSlice(srcRect, GetStoredFrameCollisionAdjust(x, y));

                if (slice.HasValue)
                    _tileCache[x, y] = slice.Value;
            }
        }
    }

    /// <summary>
    /// Clears and disposes all cached tile bitmaps and images, releasing their resources.
    /// </summary>
    internal void ClearTileCache()
    {
        if (_tileCache == null)
            return;

        for (int y = 0; y < _tileCache.GetLength(1); y++)
        {
            for (int x = 0; x < _tileCache.GetLength(0); x++)
            {
                _tileCache[x, y]?.Bitmap.Dispose();
                _tileCache[x, y]?.Image.Dispose();
                _tileCache[x, y] = null;
            }
        }

        _tileCache = null;
    }

    #endregion internal methods

    #region private methods

    private Rectangle GetTileBounds(int xTile, int yTile)
    {
        int x = _area.X + _regionMargin.Left + (xTile * TileWidthIncludingPadding);
        int y = _area.Y + _regionMargin.Top + (yTile * TileHeightIncludingPadding);

        return new Rectangle(x + _tilePadding.Left, y + _tilePadding.Top, _tileSize.Width, _tileSize.Height);
    }

    private TilesheetRegionSlice? CreateSlice(Rectangle srcRect, CollisionAdjust collisionAdjust)
    {
        var srcInfo = Tilesheet.SkBitmap.Info;

        var sliceInfo = new SKImageInfo(
            srcRect.Width,
            srcRect.Height,
            srcInfo.ColorType,
            srcInfo.AlphaType);

        var bmp = new SKBitmap(sliceInfo);
        bmp.Erase(SKColors.Transparent);

        if (!Tilesheet.SkBitmap.ExtractSubset(bmp, srcRect.ToSKRectI()))
        {
            bmp.Dispose();
            return null;
        }

        var img = SKImage.FromBitmap(bmp);

        return new TilesheetRegionSlice(bmp, img, collisionAdjust);
    }

    private CollisionAdjust GetStoredFrameCollisionAdjust(int x, int y) =>
        _frameCollisionAdjustments.TryGetValue((x, y), out var collisionAdjust)
            ? collisionAdjust
            : _collisionAdjust;

    private bool IsFrameCoordinateValid(int x, int y) =>
        _tileCache != null &&
        (uint)x < (uint)_tileCache.GetLength(0) &&
        (uint)y < (uint)_tileCache.GetLength(1);

    private void ApplyDefaultCollisionAdjustToCache()
    {
        if (_tileCache == null)
            return;

        for (int y = 0; y < _tileCache.GetLength(1); y++)
        {
            for (int x = 0; x < _tileCache.GetLength(0); x++)
            {
                if (_frameCollisionAdjustments.ContainsKey((x, y)))
                    continue;

                var slice = _tileCache[x, y];

                if (slice.HasValue)
                    _tileCache[x, y] = slice.Value.WithCollisionAdjust(_collisionAdjust);
            }
        }
    }

    private void PruneFrameCollisionAdjustments(int columns, int rows)
    {
        foreach (var key in _frameCollisionAdjustments.Keys.ToArray())
        {
            if ((uint)key.x >= (uint)columns || (uint)key.y >= (uint)rows)
                _frameCollisionAdjustments.Remove(key);
        }
    }

    private void PruneFrameCollisionTypes(int columns, int rows)
    {
        foreach (var key in _frameCollisionTypes.Keys.ToArray())
        {
            if ((uint)key.x >= (uint)columns || (uint)key.y >= (uint)rows)
                _frameCollisionTypes.Remove(key);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TilesheetRegion));
    }

    #endregion private methods

    #region IDisposable

    /// <summary>
    /// Releases all resources used by this TilesheetRegion, including the tile cache.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        ClearTileCache();
        _frameCollisionAdjustments.Clear();
        _frameCollisionTypes.Clear();
    }

    #endregion IDisposable
}