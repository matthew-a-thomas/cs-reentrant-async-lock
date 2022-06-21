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
}