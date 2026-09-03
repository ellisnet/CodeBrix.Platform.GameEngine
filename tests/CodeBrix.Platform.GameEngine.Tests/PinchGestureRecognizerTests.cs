using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CodeBrix.Platform.GameEngine.Input.Touch.Gestures;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Tests;

/// <summary>
/// Tests for the <see cref="PinchGestureRecognizer"/> lifecycle events and the data carried by
/// <see cref="PinchedEventArgs"/>.
/// </summary>
public class PinchGestureRecognizerTests
{
    [Fact]
    public void PinchStarted_PinchUpdated_and_PinchEnded_report_the_lifecycle_center_ids_and_scale()
    {
        //Arrange
        var input = new FakeTouchInput();
        using var pinch = new PinchGestureRecognizer(input);
        var events = new List<PinchedEventArgs>();
        pinch.PinchStarted += (_, e) => events.Add(e);
        pinch.PinchUpdated += (_, e) => events.Add(e);
        pinch.PinchEnded += (_, e) => events.Add(e);

        //Act
        input.Begin(2, new Point(0, 0), 100);
        input.Begin(5, new Point(10, 0), 101);
        input.Move(5, new Point(20, 0), 102);
        input.End(5, new Point(20, 0), 103);

        //Assert
        events.Select(e => e.Phase)
            .Should().Equal(PinchPhase.Began, PinchPhase.Updated, PinchPhase.Ended);
        events[1].TouchIds.Should().Equal(2, 5);
        events[0].Center.Should().Be(new PointF(5, 0));
        events[1].Center.Should().Be(new PointF(10, 0));
        events[1].ScaleDelta.Should().BeApproximately(2.0, 0.000001);
        events[1].TotalScale.Should().BeApproximately(2.0, 0.000001);
    }

    [Fact]
    public void PinchedEventArgs_two_argument_constructor_reports_the_Updated_phase()
    {
        //Arrange + Act
        var args = new PinchedEventArgs(scaleDelta: 2.0, currentDistance: 40);

        //Assert
        args.Phase.Should().Be(PinchPhase.Updated);
        args.ScaleDelta.Should().Be(2.0);
        args.CurrentDistance.Should().Be(40);
        args.PreviousDistance.Should().Be(20);
        args.TouchIds.Should().BeEmpty();
    }
}
