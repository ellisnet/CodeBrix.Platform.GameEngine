using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace CodeBrix.Platform.GameEngine.Host.Links;

/// <summary>
/// Opens links outside the game - a web page in the player's browser, a <c>mailto:</c> address - through the
/// CodeBrix.Platform launcher (<c>Windows.System.Launcher.LaunchUriAsync</c>), from any thread.
/// </summary>
/// <remarks>
/// <para>
/// The launcher must run on the UI thread. Called there, the link is launched directly; called from the
/// engine thread (a click handled in game logic, say), the launch is posted to the UI thread through
/// <see cref="Engine.UiDispatcher"/>, so the engine must have been started.
/// </para>
/// <para>
/// The returned task never faults for a link that cannot be opened: it completes with
/// <see langword="false"/> when the URI is relative, there is no UI thread to post to, or the launcher
/// refuses the link or fails (no browser, no handler for the scheme). A credits screen can show "No browser
/// was available." on <see langword="false"/>. Awaiting the task from the engine thread is fine; never
/// block the UI thread on it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// bool opened = await ExternalLinks.OpenAsync(new Uri("https://example.com"));
/// </code>
/// </example>
public static class ExternalLinks
{
    private static readonly ExternalLinkOpener Opener = new(
        IsOnUiThread,
        PostToUiThread,
        async uri => await Windows.System.Launcher.LaunchUriAsync(uri));

    /// <summary>
    /// Opens <paramref name="uri"/> with the platform launcher on the UI thread.
    /// </summary>
    /// <param name="uri">The absolute URI to open.</param>
    /// <returns>
    /// A task that completes with <see langword="true"/> when the launcher took the link, and with
    /// <see langword="false"/> when it could not be opened (see the class remarks).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public static Task<bool> OpenAsync(Uri uri) => Opener.OpenAsync(uri);

    /// <summary>
    /// Opens <paramref name="url"/> with the platform launcher on the UI thread; a string that is not an
    /// absolute URI completes with <see langword="false"/>.
    /// </summary>
    /// <param name="url">The absolute URI to open, as text.</param>
    /// <returns>
    /// A task that completes with <see langword="true"/> when the launcher took the link, and with
    /// <see langword="false"/> when it could not be opened (see the class remarks).
    /// </returns>
    public static Task<bool> OpenAsync(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Opener.OpenAsync(uri)
            : Task.FromResult(false);

    private static bool IsOnUiThread() => DispatcherQueue.GetForCurrentThread() is { HasThreadAccess: true };

    private static bool PostToUiThread(Action action)
    {
        var dispatcher = Engine.Instance.UiDispatcher;
        if (dispatcher is null)
            return false;

        dispatcher.Post(action);
        return true;
    }
}
