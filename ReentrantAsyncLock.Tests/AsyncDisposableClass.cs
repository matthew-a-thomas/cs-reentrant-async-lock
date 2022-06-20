namespace ReentrantAsyncLock.Tests;

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class AsyncDisposableClass
{
    public class DisposeAsyncMethodShould
    {
        [Fact]
        public void InvokeInjectedDelegate()
        {
            var invoked = false;
            var asyncDisposable = AsyncDisposable.Create(() =>
            {
                invoked = true;
                return default;
            });
            var _ = asyncDisposable.DisposeAsync();
            Assert.True(invoked);
        }

        [Fact]
        public void NotInvokeInjectedDelegateMoreThanOnce()
        {
            var count = 0;
            var asyncDisposable = AsyncDisposable.Create(() =>
            {
                Interlocked.Increment(ref count);
                return default;
            });
            for (var i = 0; i < 1000; ++i)
            {
                var _ = asyncDisposable.DisposeAsync();
            }
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task ReleaseStrongReferencesHeldThroughDelegate()
        {
            var (handle, asyncDisposable) = await Task.Run(() =>
            {
                var o = new object();
                return (
                    GCHandle.Alloc(o, GCHandleType.Weak),
                    AsyncDisposable.Create(() =>
                    {
                        GC.KeepAlive(o);
                        return default;
                    })
                );
            });
            var _ = asyncDisposable.DisposeAsync();
            GC.Collect();
            Assert.Null(handle.Target);
        }
    }

    public class DocumentationShould
    {
        [Fact]
        public async Task BeCorrect()
        {
            var asyncLock = new ReentrantAsyncLock();
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
}