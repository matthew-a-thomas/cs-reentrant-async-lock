namespace ReentrantAsyncLock
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An asynchronous version of the <c>lock</c> statement that supports asynchronicity, reentrance, and mutual
    /// exclusion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is powered by three concepts in asynchronous C#: <see cref="ExecutionContext"/>,
    /// <see cref="SynchronizationContext"/>, and awaitable expressions. <see cref="ExecutionContext"/> automatically
    /// flows down asynchronous code paths and allows this class to be reentrant. <see cref="SynchronizationContext"/>
    /// also automatically flows down asynchronous code paths; a special implementation serializes continuations and
    /// makes this class support mutual exclusion. And <see cref="SynchronizationContext"/> and a special awaitable type
    /// together are used to get fine-grained control over how asynchronous continuations are executed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>
    /// The following code will always succeed, and demonstrates that this class supports asynchronicity, reentrance,
    /// and mutual exclusion.
    /// </para>
    /// <code>
    /// var asyncLock = new ReentrantAsyncLock();
    /// var raceCondition = 0;
    /// // You can acquire the lock asynchronously
    /// await using (await asyncLock.LockAsync(CancellationToken.None))
    /// {
    ///     await Task.WhenAll(
    ///         Task.Run(async () =>
    ///         {
    ///             // The lock is reentrant
    ///             await using (await asyncLock.LockAsync(CancellationToken.None))
    ///             {
    ///                 // The lock provides mutual exclusion
    ///                 raceCondition++;
    ///             }
    ///         }),
    ///         Task.Run(async () =>
    ///         {
    ///             await using (await asyncLock.LockAsync(CancellationToken.None))
    ///             {
    ///                 raceCondition++;
    ///             }
    ///         })
    ///     );
    /// }
    /// Assert.Equal(2, raceCondition);
    /// </code>
    /// </example>
    public sealed class ReentrantAsyncLock
    {
        readonly AsyncLocal<object> _asyncLocalScope = new AsyncLocal<object>();
        static readonly Action<object> CancelTcs = state =>
        {
            var tcs = (TaskCompletionSource<object?>)state!;
            tcs.TrySetCanceled();
        };
        ulong _count;
        readonly object _gate = new object();
        TaskCompletionSource<object?>? _pending;
        readonly WorkQueue _queue = new WorkQueue();
        object? _owningScope;

        /// <summary>
        /// Indicates if the currently executing thread is the same thread that is processing work posted to the
        /// <see cref="SynchronizationContext"/> that is used internally.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This should always return <c>true</c> when you're in the guarded section of this
        /// <see cref="ReentrantAsyncLock"/>, and should always return <c>false</c> otherwise.
        /// </para>
        /// <para>
        /// This is probably only useful for testing. The <see cref="SynchronizationContext"/> that is used internally
        /// is designed to serialize work that is posted to it. So if you're on that thread then you're on the only
        /// thread that can do work on that <see cref="SynchronizationContext"/>, and this
        /// <see cref="ReentrantAsyncLock"/> is able to satisfy the demands placed on it.
        /// </para>
        /// </remarks>
        public bool IsOnQueue => Environment.CurrentManagedThreadId == _queue.CurrentThreadId;

        [DisallowNull]
        object? LocalScope
        {
            get => _asyncLocalScope.Value;
            set => _asyncLocalScope.Value = value;
        }

        /// <summary>
        /// Asynchronously enters the guarded section of this <see cref="ReentrantAsyncLock"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Dispose of the returned <see cref="IAsyncDisposable"/> when you want to leave the guarded section.
        /// </para>
        /// </remarks>
        public AsyncLockResult<IAsyncDisposable> LockAsync(CancellationToken cancellationToken)
        {
            LocalScope ??= new object();
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(_queue);
            var task = LockAsyncCore(previousContext, cancellationToken);
            var taskAwaiter = task.GetAwaiter();
            return new AsyncLockResult<IAsyncDisposable>(
                taskAwaiter,
                cancellationToken
            );
        }

        async Task<IAsyncDisposable> LockAsyncCore(
            SynchronizationContext? previousContext,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var task = TryLockImmediately();
                if (task is null)
                {
                    return AsyncDisposable.Create(() =>
                    {
                        Unlock();
                        if (SynchronizationContext.Current == _queue)
                            SynchronizationContext.SetSynchronizationContext(previousContext);
                        return new ValueTask(
                            new YieldToSynchronizationContextValueTaskSource(previousContext),
                            default
                        );
                    });
                }
                if (cancellationToken.CanBeCanceled)
                {
                    var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    await using (cancellationToken.Register(CancelTcs, tcs))
                    {
                        await await Task.WhenAny(
                            task,
                            tcs.Task
                        );
                    }
                }
                else
                {
                    await task;
                }
            }
        }

        Task? TryLockImmediately()
        {
            lock (_gate)
            {
                if (_count == 0)
                {
                    // No one has claimed this yet
                    ++_count;
                    _owningScope = LocalScope;
                    return null;
                }
                if (_owningScope == LocalScope)
                {
                    // We're in the same execution context as (or a descendant of) the one that acquired the lock
                    ++_count;
                    return null;
                }

                // We're on some other context and it isn't available yet. We'll need to wait our turn
                _pending ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _pending.Task;
            }
        }

        void Unlock()
        {
            lock (_gate)
            {
                --_count;
                if (_count != 0)
                    return;
                // We were the last one out. Now nobody owns the lock
                _owningScope = null;
                if (_pending is null)
                    return;
                // There are others waiting to get in
                _pending.TrySetResult(null);
                _pending = null;
            }
        }
    }
}