using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using CodeBrix.Platform.GameEngine.Drawing.Direct;
using CodeBrix.Platform.GameEngine.Rendering.Backbuffers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Regression tests for <see cref="DirectDrawingMovableBase"/>. The base constructor registers the
/// instance with the direct-drawing manager before a derived constructor assigns
/// <see cref="DirectDrawingMovableBase.Movement"/>, so the engine thread can observe a half-built object.
/// </summary>
public class DirectDrawingMovableBaseTests
{
    [Fact]
    public void Update_before_movement_initialization_does_not_throw()
    {
        //Arrange (an instance whose constructor has not run, mimicking the construction race)
        var drawing = (UninitializedMovableDrawing)
            RuntimeHelpers.GetUninitializedObject(typeof(UninitializedMovableDrawing));

        //Act
        Action act = () => drawing.Update(1);

        //Assert
        act.Should().NotThrow();
    }

    private sealed class UninitializedMovableDrawing : DirectDrawingMovableBase
    {
        private UninitializedMovableDrawing()
            : base(null!, DirectDrawingMode.View, null, null, null, null)
        {
        }

        protected override void OnDraw(BackbufferBase backbuffer, RectangleF destRectScreen)
        {
        }
    }
}
