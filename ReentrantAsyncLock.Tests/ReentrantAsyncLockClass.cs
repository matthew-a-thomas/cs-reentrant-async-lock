namespace ReentrantAsyncLock.Tests;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Flettu.Lock;
using Xunit;

public class ReentrantAsyncLockClass
{
    public class LockAsyncMethodShould
    {
        [Fact]
        public async Task SerializeReentrantCode()
        {
            var asyncLock = new AsyncLock();
            var raceConditionActualValue = 0;
            var raceConditionExpectedValue = 0;
            using (await asyncLock.AcquireAsync(CancellationToken.None))
            {
                Task GenerateTask() => Task.Run(async () =>
                {
                    using (await asyncLock.AcquireAsync(CancellationToken.None))
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
}