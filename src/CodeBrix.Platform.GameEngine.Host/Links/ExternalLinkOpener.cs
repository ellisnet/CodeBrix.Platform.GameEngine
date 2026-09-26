using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Platform.GameEngine.Host.Links;

/// <summary>
/// The logic behind <see cref="ExternalLinks"/>, with the UI-thread check, the UI-thread post and the
/// launcher passed in, so it can be exercised without a platform head.
/// </summary>
internal sealed class ExternalLinkOpener
{
    private readonly Func<bool> _isOnUiThread;
    private readonly Func<Action, bool> _postToUiThread;
    private readonly Func<Uri, Task<bool>> _launch;

    /// <summary>Creates the opener.</summary>
    /// <param name="isOnUiThread">Returns whether the caller is on the UI thread.</param>
    /// <param name="postToUiThread">Queues work on the UI thread; returns false when it could not.</param>
    /// <param name="launch">Opens a URI; runs on the UI thread.</param>
    internal ExternalLinkOpener(Func<bool> isOnUiThread, Func<Action, bool> postToUiThread, Func<Uri, Task<bool>> launch)
    {
        _isOnUiThread = isOnUiThread ?? throw new ArgumentNullException(nameof(isOnUiThread));
        _postToUiThread = postToUiThread ?? throw new ArgumentNullException(nameof(postToUiThread));
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
    }

    /// <summary>
    /// Opens <paramref name="uri"/> with the launcher on the UI thread. The task completes with
    /// <see langword="false"/> when the URI is relative, the UI thread cannot take the work, or the launcher
    /// refuses or fails; it never faults for those.
    /// </summary>
    /// <param name="uri">The absolute URI to open.</param>
    /// <returns>A task whose result says whether the launcher took the link.</returns>
    internal Task<bool> OpenAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            Engine.Logger.LogWarning("Link '{Uri}' is not an absolute URI; nothing was opened.", uri);
            return Task.FromResult(false);
        }

        bool onUiThread;
        try
        {
            onUiThread = _isOnUiThread();
        }
        catch (Exception failure)
        {
            Engine.Logger.LogWarning(failure, "Could not tell whether the caller is on the UI thread; link {Uri} was not opened.", uri);
            return Task.FromResult(false);
        }

        if (onUiThread)
            return LaunchAsync(uri);

        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued;
        try
        {
            queued = _postToUiThread(() => _ = CompleteAsync(uri, answer));
        }
        catch (Exception failure)
        {
            Engine.Logger.LogWarning(failure, "The UI thread could not take link {Uri}.", uri);
            queued = false;
        }

        if (!queued)
            answer.TrySetResult(false);

        return answer.Task;
    }

    private async Task CompleteAsync(Uri uri, TaskCompletionSource<bool> answer) =>
        answer.TrySetResult(await LaunchAsync(uri));

    private async Task<bool> LaunchAsync(Uri uri)
    {
        try
        {
            bool opened = await _launch(uri);
            if (!opened)
                Engine.Logger.LogInformation("The launcher refused link {Uri}.", uri);
            return opened;
        }
        catch (Exception failure)
        {
            Engine.Logger.LogWarning(failure, "Link {Uri} could not be opened.", uri);
            return false;
        }
    }
}
