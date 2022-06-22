namespace ReentrantAsyncLock;

using System;
using System.Threading;
using System.Threading.Tasks.Sources;

readonly struct YieldToSynchronizationContextValueTaskSource : IValueTaskSource
{
    static readonly SynchronizationContext DefaultContext = new();
    readonly SynchronizationContext? _context;

    public YieldToSynchronizationContextValueTaskSource(SynchronizationContext? context)
    {
        _context = context;
    }

    public void GetResult(short token)
    { }

    public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Pending;

    public void OnCompleted(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        (_context ?? DefaultContext).Post(new SendOrPostCallback(continuation), state);
    }
}