using System;
using System.Collections.Generic;
using System.Drawing;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using CodeBrix.Platform.GameEngine.Rendering.Views;
using CodeBrix.Platform.GameEngine.Scenes;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Covers the two <see cref="DirectComposite"/> child-bookkeeping defects carried over from the
/// Gondwana 2.5.0 source and fixed upstream in 2.5.1: disposing a composite that still held
/// children threw, and <c>Remove</c> left the composite subscribed to the removed child.
/// <para>
/// The unsubscribe half is covered indirectly, by asserting that disposing a removed child
/// leaves the composite alone. A direct test of the leak needs a WeakReference plus
/// GC.WaitForPendingFinalizers, and that reliably DEADLOCKED this assembly's process exit
/// against SkiaSharp's own finalizers - the tests all passed and then the run never ended.
/// Do not reintroduce one.
/// </para>
/// </summary>
public class DirectCompositeTests : IDisposable
{
    // Composites and direct drawings self-register with the process-global
    // DirectDrawingManager, and the engine walks that registry every foreground cycle. Anything
    // left behind here would be updated by a LATER test's engine run against a host this fixture
    // has already disposed - which hung the whole assembly until these were cleaned up. The
    // assembly runs serially for exactly this class of reason (see AssemblyInfo.cs).
    private readonly List<IDisposable> _created = new();

    /// <summary>Disposes every drawing and composite this fixture registered globally.</summary>
    public void Dispose()
    {
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            try
            {
                _created[i].Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by a test - that is the normal path for owned children.
            }
        }

        _created.Clear();
        GC.SuppressFinalize(this);
    }

    private DirectComposite NewComposite(RenderSurfaceHostBase host)
    {
        var composite = new DirectComposite(host, DirectDrawingMode.View);
        _created.Add(composite);
        return composite;
    }

    private FakeChild NewChild(RenderSurfaceHostBase host, View view)
    {
        var child = new FakeChild(host, view);
        _created.Add(child);
        return child;
    }

    [Fact]
    public void Dispose_with_children_does_not_throw_and_disposes_them()
    {
        //Arrange
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.ViewManager.ConfigureSingleFullView();
        var view = host.ViewManager.Views[0];
        var composite = NewComposite(host);

        var first = NewChild(host, view);
        var second = NewChild(host, view);
        composite.Add(first);
        composite.Add(second);

        var firstDisposed = false;
        var secondDisposed = false;
        first.Disposing += (_, _) => firstDisposed = true;
        second.Disposing += (_, _) => secondDisposed = true;

        //Act - each child's Dispose raises Disposing, which the composite handles by removing that
        //child from the very list Dispose is walking. Enumerating the live list threw here.
        var dispose = () => composite.Dispose();

        //Assert
        dispose.Should().NotThrow();
        composite.Children.Should().BeEmpty();
        firstDisposed.Should().BeTrue();
        secondDisposed.Should().BeTrue();
    }

    [Fact]
    public void Remove_then_disposing_that_child_leaves_the_composite_intact()
    {
        //Arrange
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.ViewManager.ConfigureSingleFullView();
        var view = host.ViewManager.Views[0];
        var composite = NewComposite(host);

        var removed = NewChild(host, view);
        var kept = NewChild(host, view);
        composite.Add(removed);
        composite.Add(kept);

        //Act
        composite.Remove(removed);
        removed.Dispose();

        //Assert - the composite must be untouched by a child it no longer owns
        composite.Children.Should().ContainSingle();
        composite.Children.Should().Contain(kept);

        composite.Dispose();
    }

    [Fact]
    public void Add_rejects_a_child_belonging_to_a_different_view()
    {
        //Arrange - two views on one surface; the composite resolves its target from the first
        //child, so the second one belongs somewhere else entirely.
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.ViewManager.AddView(new Rectangle(0, 0, 32, 64), 1f, 0);
        host.ViewManager.AddView(new Rectangle(32, 0, 32, 64), 1f, 1);

        var composite = NewComposite(host);
        composite.Add(NewChild(host, host.ViewManager.Views[0]));

        //Act
        var addForeignChild = () =>
            composite.Add(NewChild(host, host.ViewManager.Views[1]));

        //Assert
        addForeignChild.Should().Throw<ArgumentException>();
        composite.View.Should().BeSameAs(host.ViewManager.Views[0]);

        composite.Dispose();
    }

    [Fact]
    public void GetDrawLocationScreen_returns_empty_for_a_view_the_composite_does_not_belong_to()
    {
        //Arrange
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.ViewManager.AddView(new Rectangle(0, 0, 32, 64), 1f, 0);
        host.ViewManager.AddView(new Rectangle(32, 0, 32, 64), 1f, 1);

        var composite = NewComposite(host);
        composite.Add(NewChild(host, host.ViewManager.Views[0]));

        //Act
        var ownView = composite.GetDrawLocationScreen(host.ViewManager.Views[0]);
        var otherView = composite.GetDrawLocationScreen(host.ViewManager.Views[1]);

        //Assert - measuring against another view would report a rectangle in the wrong space
        otherView.Should().Be(RectangleF.Empty);
        ownView.Should().NotBe(RectangleF.Empty);

        composite.Dispose();
    }

    [Fact]
    public void Emptying_a_composite_clears_its_resolved_view()
    {
        //Arrange
        using var adapter = new FakeRenderSurfaceAdapter();
        using var host = new RenderSurfaceHost<BitmapBackbuffer>(adapter);
        host.ViewManager.ConfigureSingleFullView();

        var composite = NewComposite(host);
        var child = NewChild(host, host.ViewManager.Views[0]);
        composite.Add(child);

        //Act
        composite.Remove(child);

        //Assert - an emptied composite is retargetable
        composite.View.Should().BeNull();

        composite.Dispose();
    }

    /// <summary>A render-surface adapter that presents nowhere.</summary>
    private sealed class FakeRenderSurfaceAdapter : RenderSurfaceAdapterBase, IDisposable
    {
        public FakeRenderSurfaceAdapter() : base(64, 64) { }

        public override void Present(SKImage bufferImage, SKRectI bufferRect, SKRect destRect)
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>A composite child that draws nothing and records its disposal.</summary>
    private sealed class FakeChild : DirectDrawingMovableBase
    {
        public FakeChild(RenderSurfaceHostBase host, View view)
            : base(host, DirectDrawingMode.View, null, view,
                   new Rectangle(0, 0, 8, 8), null)
        {
        }

        protected override void OnDraw(BackbufferBase backbuffer, RectangleF destRectScreen)
        {
        }
    }
}
