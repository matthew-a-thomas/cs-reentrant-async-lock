﻿namespace ReentrantAsyncLock
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Awaitable;

    /// <summary>
    /// The result of asynchronously entering the guarded section of a <see cref="ReentrantAsyncLock"/>.
    /// </summary>
    public sealed class AsyncLockResult<T> : IAwaiter<T>, IAwaitable<T, AsyncLockResult<T>>
    {
        Action? _continuation;
        int _hasCompleted;
        readonly CancellationTokenRegistration _registration;
        readonly TaskAwaiter<T> _taskAwaiter;
        readonly CancellationToken _token;

        /// <summary>
        /// Creates a new <see cref="AsyncLockResult{T}"/>.
        /// </summary>
        public AsyncLockResult(
            TaskAwaiter<T> taskAwaiter,
            CancellationToken token)
        {
            _registration = token.Register(ExecuteOnce);
            _token = token;
            _taskAwaiter = taskAwaiter;
        }

        /// <summary>
        /// Returns <c>false</c>, meaning the asynchronous continuation should always be passed to
        /// <see cref="OnCompleted"/>.
        /// </summary>
        public bool IsCompleted => false;

        void ExecuteOnce() => Interlocked.Exchange(ref _continuation, null)?.Invoke();

        /// <summary>
        /// Returns <c>this</c>.
        /// </summary>
        public AsyncLockResult<T> GetAwaiter() => this;

        /// <summary>
        /// Synchronously returns the result or throws an exception.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method isn't intended to be used directly.
        /// </para>
        /// </remarks>
        public T GetResult()
        {
            _registration.Dispose();
            _token.ThrowIfCancellationRequested();
            return _taskAwaiter.GetResult();
        }

        /// <summary>
        /// Schedules the given asynchronous continuation to be executed later at the proper time.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if this method is called more than once.</exception>
        /// <remarks>
        /// <para>
        /// This method isn't intended to be used directly.
        /// </para>
        /// <para>
        /// This method captures the current <see cref="ExecutionContext"/> and runs the given <see cref="Action"/> on
        /// it.
        /// </para>
        /// </remarks>
        public void OnCompleted(Action continuation)
        {
            var context = ExecutionContext.Capture();
            if (!ReferenceEquals(context, null))
            {
                void WrappedContinuation() =>
                    ExecutionContext.Run(
                        context,
                        state => ((Action)state!).Invoke(),
                        continuation
                    );
                UnsafeOnCompleted(WrappedContinuation);
            }
            else
            {
                UnsafeOnCompleted(continuation);
            }
        }

        /// <summary>
        /// Schedules the given asynchronous continuation to be executed later at the proper time.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if this method is called more than once.</exception>
        /// <remarks>
        /// <para>
        /// This method isn't intended to be used directly.
        /// </para>
        /// <para>
        /// This method does not capture the current <see cref="ExecutionContext"/>.
        /// </para>
        /// </remarks>
        public void UnsafeOnCompleted(Action continuation)
        {
            if (Interlocked.Exchange(ref _hasCompleted, 1) != 0)
                throw new InvalidOperationException($"This method must only be called once. {nameof(AsyncLockResult<T>)} works best if you simply `await` it.");
            _continuation = continuation;
            if (_token.IsCancellationRequested)
                ExecuteOnce();
            else
                _taskAwaiter.OnCompleted(ExecuteOnce);
        }
    }
}