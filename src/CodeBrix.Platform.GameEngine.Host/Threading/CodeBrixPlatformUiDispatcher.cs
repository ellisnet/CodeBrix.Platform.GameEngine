using System;
using System.Threading;
using CodeBrix.Platform.GameEngine;
using Microsoft.UI.Dispatching;

namespace CodeBrix.Platform.GameEngine.Host.Threading;

/// <summary>
/// An <see cref="IUiDispatcher"/> implementation backed by a CodeBrix.Platform
/// <see cref="DispatcherQueue"/>. Marshals engine work onto the UI thread.
/// </summary>
public sealed class CodeBrixPlatformUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _dispatcherQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeBrixPlatformUiDispatcher"/> class.
    /// </summary>
    /// <param name="dispatcherQueue">The CodeBrix.Platform dispatcher queue for the UI thread.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dispatcherQueue"/> is null.</exception>
    public CodeBrixPlatformUiDispatcher(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    }

    /// <summary>
    /// Creates a dispatcher for the current thread's <see cref="DispatcherQueue"/>.
    /// </summary>
    /// <returns>A dispatcher bound to the current thread, or <c>null</c> if the current thread has no dispatcher queue.</returns>
    public static CodeBrixPlatformUiDispatcher? ForCurrentThread()
    {
        var dq = DispatcherQueue.GetForCurrentThread();
        return dq is null ? null : new CodeBrixPlatformUiDispatcher(dq);
    }

    /// <inheritdoc />
    public bool IsOnUIThread => _dispatcherQueue.HasThreadAccess;

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatcherQueue.TryEnqueue(() => action());
    }

    /// <inheritdoc />
    public void Send(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        using var done = new ManualResetEventSlim(false);
        Exception? captured = null;

        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();

        if (captured is not null)
            throw new InvalidOperationException("An exception was thrown while dispatching a synchronous UI action.", captured);
    }
}
