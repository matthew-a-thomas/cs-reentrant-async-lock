namespace ReentrantAsyncLock.Tests;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class ReentrantAsyncLockClass
{
    public class LockAsyncMethodShould
    {
        [Fact]
        public async Task SupportAsynchronousReentrancy()
        {
            var asyncLock = new ReentrantAsyncLock();
            await Task.Run(async () =>
            {
                await using (await asyncLock.LockAsync(CancellationToken.None))
                {
                    await Task.Run(async () =>
                    {
                        await using (await asyncLock.LockAsync(CancellationToken.None))
                        {
                            await Task.Run(async () =>
                            {
                                await using (await asyncLock.LockAsync(CancellationToken.None))
                                {
                                    await Task.Run(async () =>
                                    {
                                        await using (await asyncLock.LockAsync(CancellationToken.None))
                                        {}
                                    });
                                }
                            });
                        }
                    });
                }
            });
        }

        [Fact]
        public async Task ProvideMutualExclusion()
        {
            var asyncLock = new ReentrantAsyncLock();
            var inGuardedSection = false;
            Task GenerateTask() => Task.Run(async () =>
            {
                await using (await asyncLock.LockAsync(CancellationToken.None))
                {
                    Assert.False(inGuardedSection);
                    inGuardedSection = true;
                    SynchronizationContext.SetSynchronizationContext(null);
                    await Task.Yield(); // Return to the task pool
                    inGuardedSection = false;
                }
            });
            for (var i = 0; i < 1000; ++i)
            {
                await Task.WhenAll(
                    GenerateTask(),
                    GenerateTask(),
                    GenerateTask(),
                    GenerateTask(),
                    GenerateTask()
                );
            }
        }

        [Fact]
        public async Task SerializeReentrantCode()
        {
            var asyncLock = new ReentrantAsyncLock();
            var raceConditionActualValue = 0;
            var raceConditionExpectedValue = 0;
            await using (await asyncLock.LockAsync(CancellationToken.None))
            {
                Task GenerateTask() => Task.Run(async () =>
                {
                    await using (await asyncLock.LockAsync(CancellationToken.None))
                    {
                        // If the code in this block is running simultaneously on multiple threads then the ++ operator
                        // is a race condition. This test detects the failure mode of that race condition--namely when
                        // one increment operation overwrites/undoes the outcome of another one. If that happens then
                        // the count won't be quite right at the end of this test.
                        raceConditionActualValue++;
                        Interlocked.Increment(ref raceConditionExpectedValue); // This will always correctly increment, even in the face of multiple threads
                        // Another (perhaps more reliable) way to detect racing threads is to assert that the currently
                        // executing thread is the same thread that is currently processing the WorkQueue used
                        // internally by the ReentrantAsyncLock.
                        Assert.True(asyncLock.IsOnQueue);

                        // Asynchronously go away for a bit
                        await Task.Yield();

                        // ...then come back and do the above stuff a second time.
                        raceConditionActualValue++;
                        Interlocked.Increment(ref raceConditionExpectedValue);
                        Assert.True(asyncLock.IsOnQueue);
                    }
                });
                for (var i = 0; i < 1000; ++i)
                {
                    await Task.WhenAll(
                        GenerateTask(),
                        GenerateTask(),
                        GenerateTask(),
                        GenerateTask(),
                        GenerateTask()
                    );
                }
            }
            Assert.Equal(
                raceConditionExpectedValue,
                raceConditionActualValue
            );
        }

        [Fact]
        public async Task LeaveQueueAfterDisposal()
        {
            var asyncLock = new ReentrantAsyncLock();
            int queueThread;
            await using(await asyncLock.LockAsync(default))
            {
                queueThread = Environment.CurrentManagedThreadId;
            }
            Assert.NotEqual(Environment.CurrentManagedThreadId, queueThread);
            Assert.False(asyncLock.IsOnQueue);
            Assert.IsNotType<WorkQueue>(SynchronizationContext.Current);
        }

        [Fact]
        [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
        public async Task SupportCancellation()
        {
            var asyncLock = new ReentrantAsyncLock();
            SynchronizationContext queue;
            await using(await asyncLock.LockAsync(default))
            {
                queue = SynchronizationContext.Current ?? throw new Exception();
            }
            var mre = new ManualResetEventSlim();
            try
            {
                var waiting = false;
                queue.Post(_ =>
                {
                    Volatile.Write(ref waiting, true);
                    mre.Wait();
                }, null);
                while (!Volatile.Read(ref waiting))
                {}
                // Now the queue is clogged up waiting for us to set the MRE
                var cts = new CancellationTokenSource();
                var mre2 = new ManualResetEventSlim();
                var disposed = false;
                var task = Task.Run(async () =>
                {
                    var lockAsync = asyncLock.LockAsync(cts.Token);
                    mre2.Set();
                    await using var _ = AsyncDisposable.Create(() =>
                    {
                        disposed = true;
                        return default;
                    });
                    return await lockAsync;
                }, CancellationToken.None);
                mre2.Wait(CancellationToken.None);
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
                Assert.True(disposed);
            }
            finally
            {
                mre.Set();
            }
        }

        [Fact]
        public async Task ProvideMutualExclusionOfNestedAsyncCode()
        {
            var asyncLock = new ReentrantAsyncLock();
            var raceConditionDetector = 0;
            async Task GenerateTask()
            {
                await using (await asyncLock.LockAsync(default))
                {
                    await Task.Run(() => ++raceConditionDetector);
                }
            }
            for (var i = 0; i < 1000; ++i)
            {
                await Task.WhenAll(
                    GenerateTask(),
                    GenerateTask(),
                    GenerateTask(),
                    GenerateTask(),
                    GenerateTask()
                );
            }
            Assert.Equal(5000, raceConditionDetector);
        }
    }

    public class DocumentationShould
    {
        [Fact]
        public async Task BeCorrect1()
        {
            var asyncLock = new ReentrantAsyncLock();
            for (var i = 0; i < 1000; ++i)
            {
                var raceCondition = 0;
                // You can acquire the lock asynchronously
                await using (await asyncLock.LockAsync(CancellationToken.None))
                {
                    await Task.WhenAll(
                        Task.Run(async () =>
                        {
                            // The lock is reentrant
                            await using (await asyncLock.LockAsync(CancellationToken.None))
                            {
                                // The lock provides mutual exclusion
                                raceCondition++;
                            }
                        }),
                        Task.Run(async () =>
                        {
                            await using (await asyncLock.LockAsync(CancellationToken.None))
                            {
                                raceCondition++;
                            }
                        })
                    );
                }
                Assert.Equal(2, raceCondition);
            }
        }

        [Fact]
        public async Task BeCorrect2()
        {
            var asyncLock = new ReentrantAsyncLock();
            await using (await asyncLock.LockAsync(CancellationToken.None))
            {
                await Task.Run(async () =>
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                    await Task.Delay(1).ConfigureAwait(false);
                });
                // This is fine; the lock still works
                Assert.True(asyncLock.IsOnQueue);
            }
        }

        [Fact]
        public async Task BeCorrect3()
        {
            var asyncLock = new ReentrantAsyncLock();
            SynchronizationContext.SetSynchronizationContext(null);
            await Task.Yield();
            // Now we're on the default thread pool synchronization context
            await using (await asyncLock.LockAsync(CancellationToken.None))
            {
                // Now we're on a special synchronization context
                Assert.NotNull(SynchronizationContext.Current);
            }
            // Now we're back on the default thread pool synchronization context
            Assert.Null(SynchronizationContext.Current);
        }
    }
}