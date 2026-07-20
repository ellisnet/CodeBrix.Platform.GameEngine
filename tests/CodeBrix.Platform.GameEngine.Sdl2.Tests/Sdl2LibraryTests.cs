using CodeBrix.Platform.GameEngine.Sdl2.Native;
using SilverAssertions;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace CodeBrix.Platform.GameEngine.Sdl2.Tests;

/// <summary>
/// Validates the native library loader shim that replaces the upstream NativeLibraryLoader package.
/// </summary>
/// <remarks>
/// These tests are written as invariants that hold whether or not SDL2 is installed on the machine
/// running them, because the loader's whole purpose is to behave sanely in both cases. Asserting
/// that SDL2 loads would turn a machine without it into a test failure, which is precisely the
/// outcome this type exists to prevent.
/// </remarks>
public class Sdl2LibraryTests
{
    [Fact]
    public void GetProbeCandidates_is_never_empty()
        => Sdl2Library.GetProbeCandidates().Should().NotBeEmpty();

    [Fact]
    public void GetProbeCandidates_offers_platform_appropriate_file_names()
    {
        //Act
        var candidates = Sdl2Library.GetProbeCandidates();

        //Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            candidates.Should().Contain("SDL2.dll");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            candidates.Any(c => c.Contains("dylib", StringComparison.Ordinal)
                || c.Contains("framework", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            candidates.Any(c => c.Contains(".so", StringComparison.Ordinal)).Should().BeTrue();
        }
    }

    [Fact]
    public void GetProbeCandidates_prefers_the_versioned_soname_on_linux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { return; }

        //Act
        var candidates = Sdl2Library.GetProbeCandidates();

        //Assert
        // The versioned soname ships in the SDL2 RUNTIME package; the unversioned symlink only
        // appears once the development package is installed. Probing the versioned name first is
        // what keeps the development package from becoming a requirement.
        candidates[0].Should().Be("libSDL2-2.0.so.0");
    }

    [Fact]
    public void GetProbeCandidates_is_stable_across_calls()
        => Sdl2Library.GetProbeCandidates().Should().BeEquivalentTo(Sdl2Library.GetProbeCandidates());

    [Fact]
    public void Load_outcome_is_self_consistent()
    {
        //Act
        bool loaded = Sdl2Library.IsLoaded;

        //Assert
        if (loaded)
        {
            Sdl2Library.LoadedLibraryName.Should().NotBeNullOrWhiteSpace();
            Sdl2Library.LoadFailureDetail.Should().BeNull();
        }
        else
        {
            Sdl2Library.LoadedLibraryName.Should().BeNull();
            Sdl2Library.LoadFailureDetail.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Loaded_library_name_is_one_of_the_probe_candidates()
    {
        if (!Sdl2Library.IsLoaded) { return; }

        Sdl2Library.GetProbeCandidates().Should().Contain(Sdl2Library.LoadedLibraryName!);
    }

    [Fact]
    public void GetFunction_returns_null_for_an_export_that_does_not_exist()
    {
        //Act
        var missing = Sdl2Library.GetFunction<Action>("SDL_ThisFunctionDoesNotExist_CodeBrix");

        //Assert
        // Returning null rather than throwing is what lets a game survive both a missing SDL2 and
        // an SDL2 too old to export something.
        missing.Should().BeNull();
    }

    [Fact]
    public void Bindings_do_not_throw_when_sdl_is_unavailable()
    {
        // Whether or not SDL2 is present, touching the bindings must not throw. Upstream assigned
        // its library handle from a static field initializer that threw when SDL2 was missing,
        // which surfaced as a TypeInitializationException from the first member touched.
        var act = () => Sdl2Native.SDL_NumJoysticks();

        act.Should().NotThrow();
    }

    [Fact]
    public void Joystick_count_is_never_negative()
        => Sdl2Native.SDL_NumJoysticks().Should().BeGreaterThanOrEqualTo(0);
}
