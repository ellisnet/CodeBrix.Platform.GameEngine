using System;
using System.Reflection;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

//An application head normally registers the platform implementations and installs the dispatcher
//overrides at startup; a host-free test process has no head, so this does both, inertly. The recipe is
//the one CodeBrix.Platform's own add-in suites use (their DispatcherInitializer.cs). Reflection is used
//deliberately: a test-only concern should not widen the framework's internal surface.

/// <summary>
/// Lets the pointer-input tests build real elements (a panel and the surface inside it) and raise real
/// routed events on them in a test process with no application head.
/// </summary>
/// <remarks>
/// Every thread reports that it has dispatcher access, and dispatched work runs inline - the right
/// semantic for synchronous unit tests that assert on immediate effects.
/// </remarks>
internal static class HeadlessPlatform
{
    /// <summary>
    /// The test collection every class that builds elements belongs to. The framework's UI state is
    /// single-threaded by design, so those classes must not run in parallel with each other.
    /// </summary>
    internal const string CollectionName = "Headless platform UI";

    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>
    /// Registers the platform implementations and the inline dispatcher once per process.
    /// </summary>
    internal static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
                return;

            var bootstrapType = Type.GetType("CodeBrix.Platform.UI.Skia.SkiaPlatformBootstrap, CodeBrix.Platform.UI");
            var ensureRegistered = bootstrapType?.GetMethod("EnsureRegistered", BindingFlags.NonPublic | BindingFlags.Static);
            if (ensureRegistered is null)
            {
                throw new InvalidOperationException(
                    "Could not find SkiaPlatformBootstrap.EnsureRegistered in CodeBrix.Platform.UI. The test "
                    + "project's platform bootstrap needs updating to match the framework.");
            }

            ensureRegistered.Invoke(null, null);

            var dispatcherType = Type.GetType(
                "CodeBrix.Platform.UI.Dispatching.Skia.DispatcherPumpSkiaPlatform, CodeBrix.Platform.UI.Dispatching");
            var hasAccessField = dispatcherType?.GetField(
                "HasThreadAccessOverride", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            var dispatchField = dispatcherType?.GetField(
                "DispatchOverride", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (hasAccessField is null || dispatchField is null)
            {
                throw new InvalidOperationException(
                    "Could not find DispatcherPumpSkiaPlatform.HasThreadAccessOverride/DispatchOverride in "
                    + "CodeBrix.Platform.UI.Dispatching. The test project's dispatcher bootstrap needs updating "
                    + "to match the framework.");
            }

            if (hasAccessField.GetValue(null) is null)
                hasAccessField.SetValue(null, (Func<bool>)(static () => true));

            if (dispatchField.GetValue(null) is null)
            {
                // DispatchOverride is Action<Action, NativeDispatcherPriority>; the enum is internal,
                // so the delegate is bound through a generic method instantiated with it.
                var priorityType = dispatchField.FieldType.GetGenericArguments()[1];
                var dispatchMethod = typeof(HeadlessPlatform)
                    .GetMethod(nameof(DispatchInline), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(priorityType);
                dispatchField.SetValue(null, Delegate.CreateDelegate(dispatchField.FieldType, dispatchMethod));
            }

            _initialized = true;
        }
    }

    private static void DispatchInline<TPriority>(Action action, TPriority priority) => action();
}
