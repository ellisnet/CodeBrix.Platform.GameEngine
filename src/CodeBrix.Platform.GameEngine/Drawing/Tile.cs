using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Animation;
using CodeBrix.Platform.GameEngine.Drawing.Collisions;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Json.Extensions.References;
using CodeBrix.Json.Extensions.Polymorphism;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Platform.GameEngine.Drawing; //was previously: Gondwana.Drawing;
/// <summary>
/// Represents an abstract base class for drawable tiles in the game engine.
/// Provides core functionality for rendering, animation, collision detection, and scene layer integration.
/// </summary>
[JsonReferenceable]
[JsonDiscriminator("$type")]
[JsonKnownType(typeof(Sprite), "sprite")]
[JsonKnownType(typeof(SceneLayerTile), "sceneLayerTile")]
public abstract class Tile : IDrawable, ICollisionEntity, IComparable<Tile>, IDisposable
{
    #region static members

    /// <summary>
    /// Gets the collection of all tiles that are currently animating in the scene.
    /// Used to track and update animated tiles during the render cycle.
    /// </summary>
    public static List<Tile> TilesAnimating { get; } = new();

    #endregion static members

    #region fields

    protected internal int zOrder = 0;
    protected internal bool visible;

    protected internal Frame frame;
    protected internal bool enableFog = false;
    protected internal Animator? animator;
    protected bool pauseAnimation;

    protected ICollider? _collider;

    private CollisionAdjust _adjustCollisionArea = CollisionAdjust.None;
    private bool _adjustCollisionAreaByFrame;
    private bool _hasAssignedFrame;
    private bool _collisionAdjustExplicitlySet;
    private TileCollisionType _collisionType = TileCollisionType.None;
    private bool _collisionTypeByFrame;
    private bool _collisionTypeExplicitlySet;
    private string? _collisionProfileName;

    #endregion fields

    #region abstract properties

    /// <summary>
    /// Gets a value indicating whether the tile's position is fixed in screen space (e.g., UI elements)
    /// or moves with the world (e.g., game objects).
    /// </summary>
    public abstract bool IsPositionFixed { get; }

    /// <summary>
    /// Gets the tile's draw location in world coordinates as a rectangle.
    /// This represents the area occupied by the tile in the game world.
    /// </summary>
    public abstract Rectangle DrawLocationWorld { get; }
    
    /// <summary>
    /// Gets the tile's position within its scene layer using the layer's coordinate system.
    /// </summary>
    public abstract PointF SceneLayerCoordinates { get; }
    
    /// <summary>
    /// Gets the scene layer that contains this tile.
    /// </summary>
    public abstract SceneLayer SceneLayer { get; }

    #endregion abstract properties

    #region IDrawable members

    /// <summary>
    /// Gets the unique identifier for this tile instance.
    /// </summary>
    [JsonInclude]
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets an optional friendly name for the tile, useful for debugging and identification.
    /// </summary>
    [JsonInclude]
    public string? Nickname { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tile is visible and should be rendered.
    /// Setting this property triggers a refresh of the tile's screen area.
    /// </summary>
    [JsonInclude]
    public virtual bool Visible
    {
        get { return visible; }
        set
        {
            visible = value;
            SceneLayer?.RefreshQueue?.AddWorldRect(DrawLocationWorld);
        }
    }

    /// <summary>
    /// Gets or sets the Z-order (depth) of the tile for rendering priority.
    /// Higher values are drawn later (on top of lower values).
    /// Setting this property triggers a refresh of the tile's screen area.
    /// </summary>
    [JsonInclude]
    public virtual int ZOrder
    {
        get { return zOrder; }
        set
        {
            zOrder = value;
            SceneLayer?.RefreshQueue?.AddWorldRect(DrawLocationWorld);
        }
    }

    /// <summary>
    /// Converts the tile's world location to screen coordinates based on the specified view.
    /// </summary>
    /// <param name="view">The view containing camera and viewport information for the transformation.</param>
    /// <returns>The tile's location in screen space as a rectangle.</returns>
    public virtual RectangleF GetDrawLocationScreen(View view)
    {
        return view.WorldRectToScreenRect(SceneLayer, DrawLocationWorld);
    }

    /// <summary>
    /// Converts the tile's collision area from world coordinates to screen coordinates.
    /// </summary>
    /// <param name="view">The view containing camera and viewport information for the transformation.</param>
    /// <returns>The tile's collision area in screen space as a rectangle.</returns>
    public virtual RectangleF GetCollisionAreaScreen(View view)
    {
        return view.WorldRectToScreenRect(SceneLayer, CollisionArea);
    }

    /// <summary>
    /// Renders the tile to the specified backbuffer at the given screen location.
    /// </summary>
    /// <param name="backbuffer">The backbuffer to render to.</param>
    /// <param name="destRectScreen">The destination rectangle in screen coordinates where the tile should be drawn.</param>
    public virtual void Draw(BackbufferBase backbuffer, RectangleF destRectScreen) => backbuffer.DrawTileFrame(this, destRectScreen);

    #endregion IDrawable members

    /// <summary>
    /// Gets the overhang dimensions (in pixels) that extend beyond the tile's primary area.
    /// This is typically used for tiles with visual elements that exceed their logical boundaries.
    /// </summary>
    [JsonIgnore]
    public virtual Spacing Overhang => frame.Overhang;

    /// <summary>
    /// Gets or sets the current frame being displayed for this tile.
    /// Setting this property triggers a refresh of both the old and new tile areas to handle size changes.
    /// </summary>
    /// <remarks>
    /// The first frame assigned to a tile supplies its default <see cref="AdjustCollisionArea"/>
    /// and <see cref="CollisionType"/>, unless the tile has already been given them explicitly.
    /// Later frame changes update those values only when the matching by-frame option
    /// (<see cref="AdjustCollisionAreaByFrame"/>, <see cref="CollisionTypeByFrame"/>) is enabled.
    /// </remarks>
    [JsonInclude]
    public virtual Frame CurrentFrame
    {
        get { return frame; }
        set
        {
            // animation might change Tile size, so add before and after
            SceneLayer?.RefreshQueue?.AddWorldRect(DrawLocationWorld);

            frame = value;

            bool hasFrame = value.Tilesheet is not null;

            if (hasFrame &&
                (_adjustCollisionAreaByFrame ||
                 (!_hasAssignedFrame && !_collisionAdjustExplicitlySet)))
            {
                SetCollisionAdjustFromFrame(value);
            }

            if (hasFrame &&
                (_collisionTypeByFrame ||
                 (!_hasAssignedFrame && !_collisionTypeExplicitlySet)))
            {
                SetCollisionTypeFromFrame(value);
            }

            _hasAssignedFrame = hasFrame;

            SceneLayer?.RefreshQueue?.AddWorldRect(DrawLocationWorld);
        }
    }

    /// <summary>
    /// Gets the animator responsible for managing frame transitions and animation sequences for this tile.
    /// </summary>
    [JsonIgnore]
    public virtual Animator TileAnimator => animator!;

    /// <summary>
    /// Gets or sets a value indicating whether the tile's animation is currently paused.
    /// </summary>
    [JsonIgnore]
    public virtual bool PauseAnimation { get; set; }

    /// <summary>
    /// Gets the collider used for collision detection with this tile.
    /// Returns null if the tile has no collision detection.
    /// </summary>
    [JsonIgnore]
    public virtual ICollider? Collider => _collider;

    /// <summary>
    /// Gets the effective collision area of the tile in world coordinates,
    /// incorporating any adjustments specified by <see cref="AdjustCollisionArea"/>.
    /// </summary>
    [JsonIgnore]
    public virtual Rectangle CollisionArea => AdjustCollisionArea.ApplyTo(DrawLocationWorld);

    /// <summary>
    /// Gets or sets a value indicating whether fog of war rendering is enabled for this tile.
    /// Setting this property triggers a refresh of the tile's screen area.
    /// </summary>
    [JsonInclude]
    public virtual bool EnableFog
    {
        get { return enableFog; }
        set
        {
            enableFog = value;
            SceneLayer?.RefreshQueue?.AddWorldRect(DrawLocationWorld);
        }
    }

    /// <summary>
    /// Used to determine polygonal area when drawing grid lines or fog.
    /// Override this property in a derived class to define custom areas for these effects.
    /// </summary>
    [JsonIgnore]
    public virtual Point[] OutlinePointsWorld => SceneLayer.CoordinateSystem.GetPolygonPts(this, false);

    /// <summary>
    /// Gets or sets a value indicating whether a frame change replaces the tile's collision
    /// adjustment with the newly selected frame's adjustment.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/>, which keeps the collision area static across
    /// animation frames after the first frame has supplied its default value.
    /// </remarks>
    [JsonInclude]
    public virtual bool AdjustCollisionAreaByFrame
    {
        get => _adjustCollisionAreaByFrame;
        set
        {
            _adjustCollisionAreaByFrame = value;

            if (value && frame.Tilesheet is not null)
                SetCollisionAdjustFromFrame(frame);
        }
    }

    /// <summary>
    /// Gets or sets the collision area adjustment values that modify the tile's collision boundaries.
    /// Use this to fine-tune the collision detection area relative to the tile's visual bounds;
    /// a positive value on any edge insets that edge, a negative value expands it.
    /// </summary>
    /// <remarks>
    /// Assigning this property marks the adjustment as explicitly set, so the tile's first frame
    /// no longer overwrites it.
    /// </remarks>
    [JsonInclude]
    public virtual CollisionAdjust AdjustCollisionArea
    {
        get => _adjustCollisionArea;
        set
        {
            _adjustCollisionArea = value;
            _collisionAdjustExplicitlySet = true;
        }
    }

    private bool _collisionsEnabled = false;

    /// <summary>
    /// Gets or sets the tile's collision behaviour. The value is seeded from the first assigned
    /// frame and may subsequently be overridden per tile.
    /// </summary>
    /// <remarks>
    /// Assigning this property marks the collision type as explicitly set, so the tile's first
    /// frame no longer overwrites it, and it keeps <see cref="CollisionsEnabled"/> in step:
    /// <see cref="TileCollisionType.None"/> disables collisions, any other value enables them and
    /// sets the collider's response type.
    /// </remarks>
    [JsonInclude]
    public virtual TileCollisionType CollisionType
    {
        get => _collisionType;
        set
        {
            _collisionTypeExplicitlySet = true;
            SetCollisionTypeCore(value);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether a frame change replaces the tile's collision type
    /// with the newly selected frame's effective collision type.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/>, which keeps the collision behaviour static across
    /// animation frames after the first frame has supplied its default value.
    /// </remarks>
    [JsonInclude]
    public virtual bool CollisionTypeByFrame
    {
        get => _collisionTypeByFrame;
        set
        {
            _collisionTypeByFrame = value;

            if (value && frame.Tilesheet is not null)
                SetCollisionTypeFromFrame(frame);
        }
    }

    /// <summary>
    /// Gets the name of the scene-level collision profile currently associated with this tile,
    /// or <see langword="null"/> when no profile has been applied.
    /// </summary>
    /// <remarks>
    /// Use <see cref="SetCollisionProfile"/> to change it. A name assigned while the tile's layer
    /// is not yet attached to a scene is retained and resolved once it is.
    /// </remarks>
    [JsonInclude]
    public string? CollisionProfileName
    {
        get => _collisionProfileName;
        private set => _collisionProfileName = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether collision detection is enabled for this tile.
    /// When set to true, the tile's collider is registered with the scene layer's collision system.
    /// When set to false, the collider is unregistered and collisions will not be detected.
    /// </summary>
    /// <remarks>
    /// This flag is a projection of <see cref="CollisionType"/>: enabling collisions on a tile whose
    /// type is <see cref="TileCollisionType.None"/> promotes it to
    /// <see cref="TileCollisionType.Trigger"/> when its collider already responds as a trigger, and
    /// to <see cref="TileCollisionType.Blocking"/> otherwise; disabling collisions resets the type
    /// to <see cref="TileCollisionType.None"/>.
    /// </remarks>
    [JsonInclude]
    public bool CollisionsEnabled
    {
        get => _collisionsEnabled;
        set
        {
            _collisionTypeExplicitlySet = true;

            if (value)
            {
                var enabledType = _collisionType == TileCollisionType.None
                    ? _collider?.ResponseType == CollisionResponseType.Trigger
                        ? TileCollisionType.Trigger
                        : TileCollisionType.Blocking
                    : _collisionType;

                SetCollisionTypeCore(enabledType);
            }
            else
            {
                SetCollisionTypeCore(TileCollisionType.None);
            }
        }
    }

    /// <summary>
    /// Gets the value bag for storing arbitrary typed values associated with this tile.
    /// Useful for attaching custom game-specific data without subclassing.
    /// </summary>
    [JsonIgnore]
    public TypedValueBag ValueBag { get; } = new();

    /// <summary>
    /// Applies a named collision profile from this tile's parent scene, which supplies the
    /// collider's collision group and interaction mask.
    /// </summary>
    /// <param name="profileName">The name of a profile in the scene's collision profile registry.</param>
    /// <remarks>
    /// When the tile's layer is not yet attached to a scene the name is simply retained, and it is
    /// resolved later — when the layer joins a scene, or when a collider is attached.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="profileName"/> is null or whitespace.</exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when the tile's scene has no profile registered under that name, or the profile names
    /// a collision group the scene does not define.
    /// </exception>
    public void SetCollisionProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            throw new ArgumentException("Collision profile name cannot be empty.", nameof(profileName));

        var scene = SceneLayer?.Scene;

        if (scene is not null)
        {
            var profile = scene.CollisionProfiles.Get(profileName);
            _ = profile.ResolveCollisionGroup(scene.CollisionGroups);
            _ = profile.ResolveCollidesWith(scene.CollisionGroups);
        }

        CollisionProfileName = profileName;
        ApplyCollisionProfileToCollider();
    }

    /// <summary>
    /// Resolves the retained profile name again after the tile's layer becomes attached to a scene.
    /// </summary>
    internal void RefreshCollisionProfile() => ApplyCollisionProfileToCollider();

    /// <summary>
    /// Copies the collision behaviour, profile and effective collision adjustment from another
    /// tile, so that a clone collides exactly like its source.
    /// </summary>
    /// <param name="source">The tile whose collision settings are copied.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    protected void CopyCollisionSettingsFrom(Tile source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _adjustCollisionArea = source._adjustCollisionArea;
        _adjustCollisionAreaByFrame = source._adjustCollisionAreaByFrame;
        _hasAssignedFrame = frame.Tilesheet is not null;
        _collisionAdjustExplicitlySet = source._collisionAdjustExplicitlySet;
        _collisionType = source._collisionType;
        _collisionTypeByFrame = source._collisionTypeByFrame;
        _collisionTypeExplicitlySet = source._collisionTypeExplicitlySet;
        _collisionsEnabled = source._collisionsEnabled;
        _collisionProfileName = source._collisionProfileName;
    }

    /// <summary>
    /// Restores the frame-driven collision state a deserialized tile is missing. The save file
    /// carries <see cref="AdjustCollisionArea"/>, <see cref="AdjustCollisionAreaByFrame"/>,
    /// <see cref="CollisionType"/> and <see cref="CollisionTypeByFrame"/> but not the private
    /// flags, so a by-frame tile re-derives those values from its current frame.
    /// </summary>
    internal void RehydrateCollisionAdjustAfterDeserialization()
    {
        _hasAssignedFrame = frame.Tilesheet is not null;

        if (_adjustCollisionAreaByFrame && _hasAssignedFrame)
            SetCollisionAdjustFromFrame(frame);

        if (_collisionTypeByFrame && _hasAssignedFrame)
            SetCollisionTypeFromFrame(frame);
    }

    /// <summary>
    /// Attaches this tile's collider and applies the collision state that was configured before
    /// the collider became available: the retained profile, the collision type, and registration
    /// with the layer's collider registry.
    /// </summary>
    /// <param name="collider">The collider to attach.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="collider"/> is null.</exception>
    protected void AttachCollider(ICollider collider)
    {
        ArgumentNullException.ThrowIfNull(collider);

        if (_collider is not null)
            SceneLayer?.ColliderRegistry?.Unregister(_collider);

        _collider = collider;

        ApplyCollisionProfileToCollider();
        ApplyCollisionTypeToCollider();
        SynchronizeColliderRegistration();
    }

    private void SetCollisionAdjustFromFrame(Frame value)
    {
        // Deliberately bypass the public setter: following a frame must not mark the
        // adjustment as an explicit tile-level override.
        _adjustCollisionArea = value.CollisionAdjust;
    }

    private void SetCollisionTypeFromFrame(Frame value)
    {
        // Deliberately bypass the public setter: following a frame must not mark the
        // collision type as an explicit tile-level override.
        SetCollisionTypeCore(value.CollisionType);
    }

    private void SetCollisionTypeCore(TileCollisionType collisionType)
    {
        _collisionType = collisionType;

        ApplyCollisionTypeToCollider();
        SetCollisionsEnabledCore(collisionType != TileCollisionType.None);
    }

    private void ApplyCollisionTypeToCollider()
    {
        if (_collider is null)
            return;

        switch (_collisionType)
        {
            case TileCollisionType.Blocking:
                _collider.ResponseType = CollisionResponseType.Solid;
                break;

            case TileCollisionType.Trigger:
                _collider.ResponseType = CollisionResponseType.Trigger;
                break;
        }
    }

    private void SetCollisionsEnabledCore(bool enabled)
    {
        _collisionsEnabled = enabled;

        SynchronizeColliderRegistration();
    }

    private void SynchronizeColliderRegistration()
    {
        // A deserialized tile has no collider yet; SceneLayer.RehydrateAfterDeserialization
        // rebuilds it and AttachCollider registers per this flag.
        if (_collider is null)
            return;

        if (_collisionsEnabled)
            SceneLayer?.ColliderRegistry?.Register(_collider);
        else
            SceneLayer?.ColliderRegistry?.Unregister(_collider);
    }

    private void ApplyCollisionProfileToCollider()
    {
        if (_collider is null || string.IsNullOrWhiteSpace(_collisionProfileName))
            return;

        var scene = SceneLayer?.Scene;

        if (scene is null)
            return;

        var profile = scene.CollisionProfiles.Get(_collisionProfileName);

        _collider.CollisionGroup = profile.ResolveCollisionGroup(scene.CollisionGroups);
        _collider.CollidesWith = profile.ResolveCollidesWith(scene.CollisionGroups);
    }

    #region IComparable<Tile> Members

    /// <summary>
    /// Compares this tile to another tile for sorting purposes.
    /// Fixed-position tiles are rendered first, followed by tiles sorted by Y-coordinate, Z-order, and X-coordinate.
    /// </summary>
    /// <param name="tile">The tile to compare with this instance.</param>
    /// <returns>
    /// A negative value if this tile should be drawn before the other tile,
    /// zero if they have the same draw order,
    /// or a positive value if this tile should be drawn after the other tile.
    /// </returns>
    public int CompareTo(Tile? tile)
    {
        if (tile is null)
            return -1;

        float thisLoc = GetTileLocForCompare(this);
        float tileLoc = GetTileLocForCompare(tile);

        // Handle fixed position vs non-fixed first
        if (IsPositionFixed && !tile.IsPositionFixed)
            return -1;

        if (!IsPositionFixed && tile.IsPositionFixed)
            return 1;

        // Use tuple comparison for the rest (Y, Z, X)
        return (thisLoc, zOrder, SceneLayerCoordinates.X)
             .CompareTo((tileLoc, tile.zOrder, tile.SceneLayerCoordinates.X));
    }

    /// <summary>
    /// if position is fixed, use top of primary (i.e., non-overhanging) area;
    /// otherwise, use bottom of location for comparison
    /// </summary>
    private static float GetTileLocForCompare(Tile tile)
    {
        return tile.IsPositionFixed
            ? tile.DrawLocationWorld.Top + tile.Overhang.Top
            : tile.DrawLocationWorld.Bottom - tile.Overhang.Bottom - 1;
    }
    #endregion IComparable<Tile> Members

    #region IDisposable Members

    /// <summary>
    /// Releases all resources used by the tile, including removing it from animation tracking,
    /// disposing its animator, and clearing collision references.
    /// </summary>
    public virtual void Dispose()
    {
        if (TilesAnimating.IndexOf(this) != -1)
            TilesAnimating.Remove(this);

        // dispose any associate Animator instances
        if (animator != null)
            animator.Dispose();

        if (_collider is not null)
            SceneLayer?.ColliderRegistry?.Unregister(_collider);

        _collider = null;
    }

    #endregion IDisposable Members
}