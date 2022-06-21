/*
 * Copied with minimal changes from https://github.com/StephenCleary/AsyncEx/blob/v4/Source/Unit%20Tests/AdvancedExamples/RecursiveAsyncLockExample.cs
 */
#nullable disable
namespace ReentrantAsyncLock.Tests
{
    using System;
    using System.Collections.Immutable;
    using System.Runtime.CompilerServices;
    using System.Runtime.Remoting.Messaging;
    using System.Threading;
    using System.Threading.Tasks;
    using Nito.AsyncEx;

    // Use a logical call context to track which locks the current async logical stack frame "owns".
        // NOTE: This approach will *only* work on .NET 4.5!
        static class AsyncLockTracker
        {
            private static readonly string slotName = Guid.NewGuid().ToString("N");

            private static IImmutableDictionary<RecursiveAsyncLock, Tuple<int, Task<IDisposable>>> OwnedLocks
            {
                get
                {
                    var ret = CallContext.LogicalGetData(slotName) as ImmutableDictionary<RecursiveAsyncLock, Tuple<int, Task<IDisposable>>>;
                    if (ret == null)
                        return ImmutableDictionary.Create<RecursiveAsyncLock, Tuple<int, Task<IDisposable>>>();
                    return ret;
                }

                set
                {
                    CallContext.LogicalSetData(slotName, value);
                }
            }

            public static bool Contains(RecursiveAsyncLock mutex)
            {
                return OwnedLocks.ContainsKey(mutex);
            }

            public static Task<IDisposable> Lookup(RecursiveAsyncLock mutex)
            {
                Tuple<int, Task<IDisposable>> value;
                if (OwnedLocks.TryGetValue(mutex, out value))
                    return value.Item2;
                return null;
            }

            public static void Add(RecursiveAsyncLock mutex, Task<IDisposable> key)
            {
                Tuple<int, Task<IDisposable>> value;
                if (!OwnedLocks.TryGetValue(mutex, out value))
                    value = Tuple.Create(0, key);
                OwnedLocks = OwnedLocks.SetItem(mutex, Tuple.Create(value.Item1 + 1, value.Item2));
            }

            public static void Remove(RecursiveAsyncLock mutex)
            {
                var value = OwnedLocks[mutex];
                if (value.Item1 == 1)
                {
                    OwnedLocks = OwnedLocks.Remove(mutex);
                    value.Item2.Result.Dispose();
                }
                else
                {
                    OwnedLocks = OwnedLocks.SetItem(mutex, Tuple.Create(value.Item1 - 1, value.Item2));
                }
            }
        }

        public sealed class RecursiveAsyncLock
        {
            private readonly AsyncLock mutex;

            public RecursiveAsyncLock()
            {
                mutex = new AsyncLock();
            }

            public RecursiveAsyncLock(IAsyncWaitQueue<IDisposable> queue)
            {
                mutex = new AsyncLock(queue);
            }

            public int Id { get { return mutex.Id; } }

            public RecursiveLockAwaitable LockAsync(CancellationToken token)
            {
                var key = AsyncLockTracker.Lookup(this);
                if (key == null)
                    key = mutex.LockAsync(token).AsTask();
                return new RecursiveLockAwaitable(key, this);
            }

            public RecursiveLockAwaitable LockAsync()
            {
                return LockAsync(CancellationToken.None);
            }

            public sealed class RecursiveLockAwaitable : INotifyCompletion
            {
                private readonly Task<IDisposable> _key;
                private readonly TaskAwaiter<IDisposable> _awaiter;
                private readonly RecursiveAsyncLock _mutex;

                public RecursiveLockAwaitable(Task<IDisposable> key, RecursiveAsyncLock mutex)
                {
                    _key = key;
                    _awaiter = key.GetAwaiter();
                    _mutex = mutex;
                }

                public RecursiveLockAwaitable GetAwaiter()
                {
                    return this;
                }

                public bool IsCompleted
                {
                    get { return _awaiter.IsCompleted; }
                }

                public IDisposable GetResult()
                {
                    var ret = _awaiter.GetResult();
                    return new KeyDisposable(_key, _mutex);
                }

                public void OnCompleted(Action continuation)
                {
                    _awaiter.OnCompleted(continuation);
                }

                private sealed class KeyDisposable : IDisposable
                {
                    private RecursiveAsyncLock _mutex;

                    public KeyDisposable(Task<IDisposable> keyTask, RecursiveAsyncLock mutex)
                    {
                        _mutex = mutex;
                        AsyncLockTracker.Add(mutex, keyTask);
                    }

                    public void Dispose()
                    {
                        if (_mutex == null)
                            return;
                        AsyncLockTracker.Remove(_mutex);
                        _mutex = null;
                    }
                }
            }
        }
}