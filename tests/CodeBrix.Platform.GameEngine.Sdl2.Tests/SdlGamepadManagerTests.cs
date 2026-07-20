using CodeBrix.Platform.GameEngine.Input.Gamepad;
using CodeBrix.Platform.GameEngine.Sdl2.Gamepad;
using SilverAssertions;
using System;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Sdl2.Tests;

/// <summary>
/// Validates the gamepad manager's availability reporting and lifecycle.
/// </summary>
/// <remarks>
/// Like the loader tests, these are invariants that hold with or without SDL2 installed and with or
/// without a controller attached. The polling of real hardware cannot be asserted here; that is
/// what the on-device verification run covers.
/// </remarks>
public class SdlGamepadManagerTests
{
    [Fact]
    public void TryStart_never_throws()
    {
        //Act
        var act = () =>
        {
            SdlGamepadManager.TryStart(out SdlGamepadManager manager);
            manager.Dispose();
        };

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void TryStart_always_produces_a_manager()
    {
        //Act
        SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        //Assert
        // A manager comes back even on failure, so that a game has one object to ask why gamepad
        // support is missing rather than having to work it out for itself.
        manager.Should().NotBeNull();
        manager.Dispose();
    }

    [Fact]
    public void TryStart_result_agrees_with_the_reported_availability()
    {
        //Act
        bool started = SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        //Assert
        try
        {
            manager.IsAvailable.Should().Be(started);
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public void Availability_and_unavailable_reason_are_consistent()
    {
        //Act
        SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        //Assert
        try
        {
            if (manager.IsAvailable)
            {
                manager.UnavailableReason.Should().BeNull();
                manager.UnavailableCause.Should().Be(SdlGamepadUnavailableCause.None);
            }
            else
            {
                manager.UnavailableReason.Should().NotBeNullOrWhiteSpace();
                manager.UnavailableCause.Should().NotBe(SdlGamepadUnavailableCause.None);
            }
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public void ConnectedAdapters_returns_the_same_live_instance_each_time()
    {
        //Act
        SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        //Assert
        try
        {
            // The engine's gamepad event poller captures this collection ONCE, when the manager is
            // assigned. Handing back a fresh copy per call would leave the poller holding a
            // snapshot that never sees a controller connected later.
            manager.ConnectedAdapters.Should().BeSameAs(manager.ConnectedAdapters);
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public void Update_does_not_throw()
    {
        //Arrange
        SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        //Act
        var act = () =>
        {
            manager.Update();
            manager.Update();
        };

        //Assert
        try
        {
            act.Should().NotThrow();
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public void Update_after_dispose_does_nothing()
    {
        //Arrange
        SdlGamepadManager.TryStart(out SdlGamepadManager manager);
        manager.Dispose();

        //Act
        var act = () => manager.Update();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        //Arrange
        SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        //Act
        var act = () =>
        {
            manager.Dispose();
            manager.Dispose();
        };

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GetNoControllersHint_is_null_when_gamepad_support_is_unavailable()
    {
        //Act
        SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        //Assert
        try
        {
            if (!manager.IsAvailable)
            {
                // With no working SDL2 there is nothing useful to say about missing controllers;
                // UnavailableReason is the thing to show instead.
                manager.GetNoControllersHint().Should().BeNull();
            }
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public void GetNoControllersHint_offers_advice_when_available_but_nothing_is_connected()
    {
        //Act
        SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        //Assert
        try
        {
            if (manager.IsAvailable && manager.ConnectedAdapters.Count == 0)
            {
                manager.GetNoControllersHint().Should().NotBeNullOrWhiteSpace();
            }
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public void Manager_satisfies_the_engine_gamepad_manager_contract()
    {
        //Act
        SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        //Assert
        try
        {
            // The engine exposes its gamepad manager as IGamepadManager<IGamepadAdapter>. This
            // assignment relies on the interface's covariance, so it is worth pinning: losing it
            // would break the engine handoff at compile time in the consuming game rather than here.
            IGamepadManager<IGamepadAdapter> asEngineContract = manager;
            asEngineContract.ConnectedAdapters.Should().NotBeNull();
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public void Connected_adapters_report_usable_identity()
    {
        //Act
        SdlGamepadManager.TryStart(out SdlGamepadManager manager);

        //Assert
        try
        {
            foreach (SdlGamepadAdapter adapter in manager.ConnectedAdapters)
            {
                adapter.GamepadId.Should().NotBeNullOrWhiteSpace();
                adapter.Name.Should().NotBeNullOrWhiteSpace();
                adapter.PressedButtons.Should().NotBeNull();
                adapter.LeftTrigger.Should().BeGreaterThanOrEqualTo(0f);
                adapter.RightTrigger.Should().BeGreaterThanOrEqualTo(0f);
            }
        }
        finally
        {
            manager.Dispose();
        }
    }
}
