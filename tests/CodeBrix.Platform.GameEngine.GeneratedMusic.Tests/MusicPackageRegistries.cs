using System;
using System.Reflection;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.MusicGeneration;

namespace CodeBrix.Platform.GameEngine.GeneratedMusic.Tests;

/// <summary>
/// Puts the music packages' two process-wide registries back to their starting state between tests.
/// </summary>
/// <remarks>
/// Both registries carry a <c>ResetForTesting()</c> documented as being for exactly this - tests that
/// share the process-wide state - but it is internal to its package, so it is reached by reflection.
/// A package that renames or removes it fails every test here loudly, rather than letting tests leak
/// registrations into one another.
/// </remarks>
internal static class MusicPackageRegistries
{
    /// <summary>
    /// Empties the generator registry back to the four built-in replays.
    /// </summary>
    internal static void ResetGenerators() => Invoke(typeof(MusicGeneratorRegistry));

    /// <summary>Empties the instrument library registry: nothing registered, no default.</summary>
    internal static void ResetInstrumentLibraries() => Invoke(typeof(InstrumentLibraryRegistry));

    private static void Invoke(Type registry)
    {
        MethodInfo reset = registry.GetMethod("ResetForTesting", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{registry.FullName} no longer has a ResetForTesting method.");

        reset.Invoke(null, null);
    }
}
