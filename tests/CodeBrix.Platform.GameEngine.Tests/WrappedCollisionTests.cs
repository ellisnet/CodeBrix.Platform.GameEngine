using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Sprites;
using CodeBrix.Platform.GameEngine.Drawing.Tilesheets;
using CodeBrix.Platform.GameEngine.Physics.Collisions;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Collision behaviour on periodic layers: overlaps are detected and resolved against the
/// translated bounds of the overlapping image, while the events that report them always carry the
/// canonical collider. Frame overhang and collision adjustment are applied to the canonical bounds
/// before translation.
/// </summary>
public class WrappedCollisionTests : IDisposable
{
    private readonly List<Scene> _scenes = new();
    private readonly List<Tilesheet> _tilesheets = new();

    /// <summary>Clears the global sprite, scene and tilesheet registries this fixture populated.</summary>
    public void Dispose()
    {
        SpriteManager.Instance.ClearImmediate();

        foreach (var scene in _scenes)
            scene.Dispose();

        _scenes.Clear();
        Scene.ClearAllScenes();

        foreach (var tilesheet in _tilesheets)
            tilesheet.Dispose();

        _tilesheets.Clear();
        GC.SuppressFinalize(this);
    }

    private SceneLayer CreateWrappedLayer(int columns, int rows, int tileWidth = 32, int tileHeight = 32)
    {
        var scene = new Scene();
        _scenes.Add(scene);

        var layer = scene.AddLayer(columns, rows, tileWidth, tileHeight);
        layer.WrapHorizontally = true;
        layer.WrapVertically = true;

        return layer;
    }

    [Theory]
    [InlineData(126, 10, false, true)]
    [InlineData(10, 126, false, true)]
    [InlineData(126, 126, false, true)]
    [InlineData(-2, -2, false, true)]
    [InlineData(126, 126, true, true)]
    [InlineData(126, 126, false, false)]
    public void Resolve_uses_translated_bounds_and_reports_the_canonical_collider(
        int x,
        int y,
        bool trigger,
        bool isStatic)
    {
        //Arrange - the mover sits over a seam; the other collider's canonical bounds do not reach it.
        var layer = CreateWrappedLayer(4, 4);

        var mover = new TestCollider(new Rectangle(x, y, 10, 10), isStatic: false);
        var other = new TestCollider(new Rectangle(0, 0, 32, 32), isStatic)
        {
            ResponseType = trigger ? CollisionResponseType.Trigger : CollisionResponseType.Solid
        };

        layer.ColliderRegistry.Register(mover);
        layer.ColliderRegistry.Register(other);

        int events = 0;

        void OnOverlap(ICollider a, ICollider b, Rectangle bounds)
        {
            if (!ReferenceEquals(a, mover))
                return;

            b.Should().BeSameAs(other);
            bounds.IsEmpty.Should().BeFalse();
            events++;
        }

        layer.CollisionResolver.TriggerOverlap += OnOverlap;
        layer.CollisionResolver.SolidOverlap += OnOverlap;

        //Act
        var before = mover.CollisionArea;
        layer.CollisionResolver.Resolve();

        //Assert - triggers report without push-out; solids are pushed out of the translated image.
        events.Should().Be(1);

        if (trigger)
            mover.CollisionArea.Should().Be(before);
        else
            mover.CollisionArea.Should().NotBe(before);
    }

    [Fact]
    public void QueryInstances_applies_frame_overhang_and_collision_adjust_before_translation()
    {
        //Arrange - a tile whose artwork overhangs its cell by 40 px on every edge.
        using var bitmap = new SKBitmap(64, 64);
        var sheet = TilesheetFactory.FromBitmap("WrappedOverhang", bitmap);
        _tilesheets.Add(sheet);
        sheet.DefaultRegion.TileSize = new Size(32, 32);
        sheet.DefaultRegion.Overhang = new Spacing(40, 40, 40, 40);

        var layer = CreateWrappedLayer(4, 4);
        var tile = layer[0, 0]!;
        tile.CurrentFrame = sheet.GetFrame(0, 0);
        tile.AdjustCollisionArea = new CollisionAdjust(-10, -10, -10, -10);
        tile.CollisionsEnabled = true;
        tile.Collider!.CollisionGroup = 1;
        tile.Collider.CollidesWith = 1;

        var area = tile.Collider.BoundsWorldPx;
        var query = new Aabb(area.MinX + 128, area.MinY + 128, area.MinX + 130, area.MinY + 130);
        var instances = new List<ColliderInstance>();

        //Act
        layer.ColliderRegistry.QueryInstances(query, 1, 1, instances);

        //Assert - the adjusted canonical bounds are translated by one whole period on each axis.
        instances.Any(i => ReferenceEquals(i.Collider, tile.Collider) && i.BoundsWorldPx.MinX == area.MinX + 128)
            .Should().BeTrue();

        //Assert - the render instance carries the same translation.
        var draws = layer.GetDrawablesInWorldRect(new Rectangle(90, 90, 2, 2)).Cast<WrappedDrawable>();
        draws.Any(d => ReferenceEquals(d.Owner, tile) && d.Offset == new PointF(128, 128)).Should().BeTrue();
    }

    private sealed class TestCollider(Rectangle bounds, bool isStatic) : ICollider, ICollisionMovableEntity
    {
        private Rectangle _bounds = bounds;

        public Aabb BoundsWorldPx => Aabb.FromRectangle(_bounds);

        public ICollisionEntity Owner => this;

        public bool IsStatic => isStatic;

        public int CollisionGroup { get; set; } = 1;

        public int CollidesWith { get; set; } = 1;

        public CollisionResponseType ResponseType { get; set; } = CollisionResponseType.Solid;

        public Rectangle CollisionArea => _bounds;

        public void TranslateWorldPx(int dx, int dy) => _bounds.Offset(dx, dy);

        public void CancelVelocityComponent(bool cancelX, bool cancelY)
        {
        }
    }
}
