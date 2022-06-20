# ReentrantAsyncLock

A reentrant asynchronous lock.

[![GitHub Workflow Status](https://img.shields.io/github/workflow/status/matthew-a-thomas/cs-reentrant-async-lock/.NET)](https://github.com/matthew-a-thomas/cs-reentrant-async-lock)

[![Nuget](https://img.shields.io/nuget/v/ReentrantAsyncLock)](https://www.nuget.org/packages/ReentrantAsyncLock)

The `ReentrantAsyncLock` class provides all three of these things:
* Reentrance
* Asynchronicity
* Mutual exclusion

# Example

```csharp
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
```

# How does it work?

This class is powered by three concepts in asynchronous C#: `ExecutionContext`;
`SynchronizationContext`; and awaitable expressions. `ExecutionContext`
automatically flows down asynchronous code paths and allows this class to be
reentrant. `SynchronizationContext` also automatically flows down asynchronous
code paths; a special implementation serializes continuations and makes this
class support mutual exclusion. And `SynchronizationContext` and a special
awaitable type together are used to get fine-grained control over how
asynchronous continuations are executed.

# More details

[https://www.matthewathomas.com/programming/2022/06/20/introducing-reentrantasynclock.html](https://www.matthewathomas.com/programming/2022/06/20/introducing-reentrantasynclock.html)