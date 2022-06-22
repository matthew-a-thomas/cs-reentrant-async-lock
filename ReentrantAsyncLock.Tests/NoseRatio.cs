namespace ReentrantAsyncLock.Tests;

using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class NoseRatio
{
    /// <summary>
    /// https://www.reddit.com/r/dotnet/comments/vhe7bo/comment/id9i6rk/?utm_source=share&utm_medium=web2x&context=3
    /// </summary>
    [Fact]
    public async Task ShouldWork1()
    {
        var monitor = new ReentrantAsyncLock();

        async Task Func1(int i)
        {
            if (i == 0) { return; }
            await using (await monitor.LockAsync(CancellationToken.None))
            {
                await Func2(i-1);
            }
        }

        async Task Func2(int i)
        {
            if (i == 0) { return; }
            await using (await monitor.LockAsync(CancellationToken.None))
            {
                await Func1(i-1);
            }
        }

        await Func1(42);
    }

    /// <summary>
    /// https://www.reddit.com/r/dotnet/comments/vhe7bo/comment/id9i6rk/?utm_source=share&utm_medium=web2x&context=3
    /// </summary>
    [Fact]
    public async Task ShouldWork2()
    {
        var monitor = new ReentrantAsyncLock();

        async Task Func1(int i)
        {
            if (i == 0) { return; }
            await using (await monitor.LockAsync(CancellationToken.None))
            {
                await Func2(i-1);
            }
        }

        async Task Func2(int i)
        {
            if (i == 0) { return; }
            await using (await monitor.LockAsync(CancellationToken.None))
            {
                await Func1(i-1);
            }
        }

        await Task.WhenAll(Func1(42), Func2(42));
    }
}