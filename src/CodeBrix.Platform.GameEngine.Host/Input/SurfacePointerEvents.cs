using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace CodeBrix.Platform.GameEngine.Host.Input;

/// <summary>
/// The pointer-event rule the game surface's input adapters share: a pointer event an adapter reads
/// is marked handled, so ancestors of the surface (a ScrollViewer, a page-level handler) do not act on
/// a game click as well, and the framework does not treat the click as one nobody wanted.
/// </summary>
/// <remarks>
/// <para>
/// Marking an event handled also hides it from the handlers attached AFTER the marking adapter on the
/// same element, the other adapters included. So the adapters subscribe with
/// <c>handledEventsToo: true</c> and tell apart an event another adapter consumed (still theirs to
/// read) from one something else handled first - a button placed inside the surface, say - which they
/// leave alone, exactly as an ordinary subscription would.
/// </para>
/// <para>
/// Only the surface's own events are touched: a click on an element outside the surface never reaches
/// these handlers, so it moves focus to whatever handles it as usual.
/// </para>
/// </remarks>
internal static class SurfacePointerEvents
{
    private static readonly object Marker = new();

    // The events an adapter has consumed; weak, so an args object is never kept alive by this table.
    private static readonly ConditionalWeakTable<PointerRoutedEventArgs, object> Consumed = new();

    /// <summary>
    /// Subscribes <paramref name="handler"/> to <paramref name="routedEvent"/> on <paramref name="element"/>
    /// so it runs whichever handler marked the event handled before it.
    /// </summary>
    /// <param name="element">The game surface.</param>
    /// <param name="routedEvent">The pointer routed event.</param>
    /// <param name="handler">The adapter's handler; keep the same instance for <see cref="Unsubscribe"/>.</param>
    internal static void Subscribe(UIElement element, RoutedEvent routedEvent, PointerEventHandler handler)
        => element.AddHandler(routedEvent, handler, handledEventsToo: true);

    /// <summary>
    /// Removes a handler added with <see cref="Subscribe"/>.
    /// </summary>
    /// <param name="element">The game surface.</param>
    /// <param name="routedEvent">The pointer routed event.</param>
    /// <param name="handler">The handler instance passed to <see cref="Subscribe"/>.</param>
    internal static void Unsubscribe(UIElement element, RoutedEvent routedEvent, PointerEventHandler handler)
        => element.RemoveHandler(routedEvent, handler);

    /// <summary>
    /// Returns <see langword="true"/> when the event is the adapters' to read: it is unhandled, or only a
    /// game-surface input adapter has marked it handled.
    /// </summary>
    /// <param name="e">The pointer event.</param>
    /// <returns><see langword="false"/> when something other than an input adapter handled the event.</returns>
    internal static bool IsAvailable(PointerRoutedEventArgs e)
        => !e.Handled || Consumed.TryGetValue(e, out _);

    /// <summary>
    /// Marks the event handled on behalf of the game.
    /// </summary>
    /// <param name="e">The pointer event the calling adapter has read.</param>
    internal static void Consume(PointerRoutedEventArgs e)
    {
        Consumed.AddOrUpdate(e, Marker);
        e.Handled = true;
    }
}
