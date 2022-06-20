namespace ReentrantAsyncLock.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

public class WorkQueueClassShould
{
    [Fact]
    public void DoWorkOneAtATimeAndInOrder()
    {
        var simpleWorkQueue = new WorkQueue();
        var items = new List<int>();
        var working = false;
        var exceptions = new List<Exception>();
        simpleWorkQueue.ExceptionThrown += exceptions.Add;
        for (var i = 0; i < 1000; ++i)
        {
            simpleWorkQueue.Post(state =>
            {
                if (working)
                    throw new Exception("Already working");
                working = true;
                items.Add((int)state!);
                working = false;
            }, i);
        }
        var manualResetEventSlim = new ManualResetEventSlim();
        simpleWorkQueue.Post(_ => manualResetEventSlim.Set(), null);
        manualResetEventSlim.Wait();
        Assert.Empty(exceptions);
        Assert.Equal(
            Enumerable.Range(0, 1000),
            items
        );
    }

    [Fact]
    public void ReportExceptions()
    {
        var simpleWorkQueue = new WorkQueue();
        var exceptions = new List<Exception>();
        simpleWorkQueue.ExceptionThrown += e => exceptions.Add(e);
        var message = Guid.NewGuid().ToString();
        simpleWorkQueue.Post(_ => throw new Exception(message), null);
        {
            var spinWait = new SpinWait();
            while (exceptions.Count == 0)
            {
                spinWait.SpinOnce();
            }
        }
        var exception = Assert.Single(exceptions);
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public async Task SerializeParallelTasks()
    {
        var simpleWorkQueue = new WorkQueue();
        var exceptions = new List<Exception>();
        simpleWorkQueue.ExceptionThrown += exceptions.Add;
        SynchronizationContext.SetSynchronizationContext(simpleWorkQueue);
        var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
        var taskFactory = new TaskFactory(scheduler);
        var list = new List<int>();
        await Task.WhenAll(Enumerable.Range(0, 1000).Select(i => taskFactory.StartNew(() =>
        {
            list.Add(i);
        })));
        Assert.Empty(exceptions);
        Assert.Equal(
            Enumerable.Range(0, 1000),
            list
        );
    }

    [Fact]
    public void SayThatItIsTheSynchronizationContextForAndOnTheSameThreadAsPostedWork()
    {
        var simpleWorkQueue = new WorkQueue();
        Assert.Null(simpleWorkQueue.CurrentThreadId);
        var exceptions = new List<Exception>();
        simpleWorkQueue.ExceptionThrown += exceptions.Add;
        var mre = new ManualResetEventSlim();
        simpleWorkQueue.Post(_ =>
        {
            try
            {
                Assert.Equal(simpleWorkQueue, SynchronizationContext.Current);
                Assert.Equal(Environment.CurrentManagedThreadId, simpleWorkQueue.CurrentThreadId);
            }
            finally
            {
                mre.Set();
            }
        }, null);
        mre.Wait();
        Assert.Empty(exceptions);
    }
}