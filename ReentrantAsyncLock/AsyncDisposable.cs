namespace ReentrantAsyncLock;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// An injected implementation of <see cref="IAsyncDisposable"/>.
/// </summary>
sealed class AsyncDisposable : IAsyncDisposable
{
    Func<ValueTask>? _disposeAsync;

    AsyncDisposable(Func<ValueTask>? disposeAsync)
    {
        _disposeAsync = disposeAsync;
    }

    /// <summary>
    /// Creates a new <see cref="AsyncDisposable"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// You can call <see cref="DisposeAsync"/> on the returned object as many times as you like and the given delegate
    /// will only be invoked up to once.
    /// </para>
    /// </remarks>
    public static AsyncDisposable Create(Func<ValueTask> disposeAsync) => new(disposeAsync);

    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _disposeAsync, null)?.Invoke() ?? default;
}