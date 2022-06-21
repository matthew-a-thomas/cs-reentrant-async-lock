﻿namespace ReentrantAsyncLock.Tests;

using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class ReentrantAsyncLockClass
{
    public class LockAsyncMethodShould
    {
        [Fact]
        public async Task SerializeReentrantCode()
        {
            var asyncLock = new RecursiveAsyncLock();
            var raceConditionActualValue = 0;
            var raceConditionExpectedValue = 0;
            using (await asyncLock.LockAsync(CancellationToken.None))
            {
                Task GenerateTask() => Task.Run(async () =>
                {
                    using (await asyncLock.LockAsync(CancellationToken.None))
                    {
                        // If the code in this block is running simultaneously on multiple threads then the ++ operator
                        // is a race condition. This test detects the failure mode of that race condition--namely when
                        // one increment operation overwrites/undoes the outcome of another one. If that happens then
                        // the count won't be quite right at the end of this test.
                        raceConditionActualValue++;
                        Interlocked.Increment(ref raceConditionExpectedValue); // This will always correctly increment, even in the face of multiple threads
                        // Another (perhaps more reliable) way to detect racing threads is to assert that the currently
                        // executing thread is the same thread that is currently processing the WorkQueue used
                        // internally by the RecursiveAsyncLock.
                        // Assert.True(asyncLock.IsOnQueue);

                        // Asynchronously go away for a bit
                        await Task.Yield();

                        // ...then come back and do the above stuff a second time.
                        raceConditionActualValue++;
                        Interlocked.Increment(ref raceConditionExpectedValue);
                        // Assert.True(asyncLock.IsOnQueue);
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
    }

    public class DocumentationShould
    {
        [Fact]
        public async Task BeCorrect1()
        {
            var asyncLock = new RecursiveAsyncLock();
            for (var i = 0; i < 1000; ++i)
            {
                var raceCondition = 0;
                // You can acquire the lock asynchronously
                using (await asyncLock.LockAsync(CancellationToken.None))
                {
                    await Task.WhenAll(
                        Task.Run(async () =>
                        {
                            // The lock is reentrant
                            using (await asyncLock.LockAsync(CancellationToken.None))
                            {
                                // The lock provides mutual exclusion
                                raceCondition++;
                            }
                        }),
                        Task.Run(async () =>
                        {
                            using (await asyncLock.LockAsync(CancellationToken.None))
                            {
                                raceCondition++;
                            }
                        })
                    );
                }
                Assert.Equal(2, raceCondition);
            }
        }
    }
}