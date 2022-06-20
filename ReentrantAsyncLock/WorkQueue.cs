namespace ReentrantAsyncLock;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// A <see cref="SynchronizationContext"/> in which units of work are executed one-at-a-time on the thread pool.
/// </summary>
sealed class WorkQueue : SynchronizationContext
{
    /// <summary>
    /// Exposes exceptions thrown on this <see cref="SynchronizationContext"/>.
    /// </summary>
    public event Action<Exception>? ExceptionThrown;

    readonly Queue<Entry> _entries = new();
    readonly object _gate = new();
    bool _isPumping;
    static readonly Action<object?> PumpDelegate;
    static readonly SendOrPostCallback SetManualResetEventSlimDelegate;
    static readonly ConcurrentBag<ManualResetEventSlim> UnusedManualResetEvents = new();

    static WorkQueue()
    {
        PumpDelegate = Pump;
        SetManualResetEventSlimDelegate = SetManualResetEventSlim;
    }

    /// <summary>
    /// Gets the ID of the thread that is currently pumping this <see cref="WorkQueue"/>, or <c>null</c> if it is
    /// not being pumped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This will be the same as <see cref="Environment.CurrentManagedThreadId"/> for all work done on this
    /// <see cref="WorkQueue"/>.
    /// </para>
    /// </remarks>
    public int? CurrentThreadId { get; private set; }

    /// <summary>
    /// Returns a new <see cref="WorkQueue"/>.
    /// </summary>
    public override SynchronizationContext CreateCopy() => new WorkQueue();

    public override void Post(SendOrPostCallback d, object? state)
    {
        var executionContext = ExecutionContext.Capture();
        lock (_gate)
        {
            _entries.Enqueue(new Entry(d, state, executionContext));
            if (_isPumping)
                return;
            _isPumping = true;
        }
        ThreadPool.QueueUserWorkItem(PumpDelegate, this, false);
    }

    static void Pump(object? state)
    {
        var me = (WorkQueue)state!;
        me.Pump();
    }

    void Pump()
    {
        CurrentThreadId = Environment.CurrentManagedThreadId;
        var oldContext = Current;
        while (true)
        {
            Entry entry;
            lock (_gate)
            {
                if (!_entries.TryDequeue(out entry))
                {
                    _isPumping = false;
                    CurrentThreadId = null;
                    SetSynchronizationContext(oldContext);
                    return;
                }
            }
            try
            {
                SetSynchronizationContext(this);
                if (entry.ExecutionContext is {} executionContext)
                {
                    ExecutionContext.Run(executionContext, new ContextCallback(entry.Callback), entry.State);
                }
                else
                {
                    entry.Callback(entry.State);
                }
            }
            catch (Exception e)
            {
                ExceptionThrown?.Invoke(e);
            }
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        Post(d, state);
        if (!UnusedManualResetEvents.TryTake(out var mre))
            mre = new ManualResetEventSlim();
        Post(SetManualResetEventSlimDelegate, mre);
        mre.Wait();
        mre.Reset();
        UnusedManualResetEvents.Add(mre);
    }

    static void SetManualResetEventSlim(object? state)
    {
        var mre = (ManualResetEventSlim)state!;
        mre.Set();
    }

    record struct Entry(SendOrPostCallback Callback, object? State, ExecutionContext? ExecutionContext);
}