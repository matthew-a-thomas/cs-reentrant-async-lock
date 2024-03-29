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

Check out
[the automated tests](https://github.com/matthew-a-thomas/cs-reentrant-async-lock/blob/main/ReentrantAsyncLock.Tests/ReentrantAsyncLockClass.cs)
for more examples of what `ReentrantAsyncLock` can do.

# Compared to other implementations

[`Cogs.Threading`](https://www.nuget.org/packages/Cogs.Threading) works: https://dotnetfiddle.net/6WdVy1<br/>
Their `ReentrantAsyncLock` is the only other working implementation that I know of. Check out that library to see if it'll work for you.

There are lots of broken reentrant asynchronous locks out there. Some will deadlock trying to re-enter the lock in one of the `Task.Run` calls. Others will not actually provide mutual exclusion and the `raceCondition` variable will sometimes equal 1 instead of 2:

* [Stephen Cleary's POC](https://github.com/StephenCleary/AsyncEx/blob/v4/Source/Unit%20Tests/AdvancedExamples/RecursiveAsyncLockExample.cs) does not provide mutual exclusion: https://dotnetfiddle.net/vLKyCX
* [NeoSmart.AsyncLock](https://github.com/neosmart/AsyncLock) does not provide reentrance with mutual exclusion: https://dotnetfiddle.net/CkK674
* [Flettu](https://github.com/mysteryjeans/Flettu/) sometimes does not provide reentrance, sometimes throws a semaphore count exception, or otherwise does not provide mutual exclusion: https://dotnetfiddle.net/o0c7j7
* [CellWars.Threading.AsyncLock](https://github.com/jasonkuo41/CellWars.Threading.AsyncLock) does not provide mutual exclusion: https://dotnetfiddle.net/Tz38lN

# How does it work?

This class is powered by three concepts in asynchronous C#: `ExecutionContext`,
`SynchronizationContext`, and awaitable expressions.

* `ExecutionContext` automatically flows down asynchronous code paths and allows
  this class to be reentrant
* `SynchronizationContext` also automatically flows down asynchronous code
  paths; a special implementation serializes continuations and makes this class
  support mutual exclusion
* A special awaitable type is used together with `SynchronizationContext` to get
  fine-grained control over how asynchronous continuations are executed

# Gotchas

These are easy to work around&mdash;keep reading and you'll see how&mdash;but
you need to be aware of them.

## Don't change the current `SynchronizationContext` once you're in the guarded section

Because this is powered by a special `SynchronizationContext` you should not
change the current `SynchronizationContext` within the guarded section. For
example, do **not** do this:

```csharp
var asyncLock = new ReentrantAsyncLock();
await using (await asyncLock.LockAsync(CancellationToken.None))
{
    SynchronizationContext.SetSynchronizationContext(null);
    await Task.Yield();
    // Now the lock is broken
}
```

Also, do not use `ConfigureAwait(false)` within the guarded section. For
example, do **not** do this:

```csharp
var asyncLock = new ReentrantAsyncLock();
await using (await asyncLock.LockAsync(CancellationToken.None))
{
    await Task.Delay(1).ConfigureAwait(false);
    // Now the lock is broken
}
```

However, it's fine if the current `SynchronizationContext` is changed or
`ConfigureAwait(false)` is used _by an awaited method_ within the guarded
section. For example, this **is fine**:

```csharp
var asyncLock = new ReentrantAsyncLock();
await using (await asyncLock.LockAsync(CancellationToken.None))
{
    await Task.Run(async () =>
    {
        SynchronizationContext.SetSynchronizationContext(null);
        await Task.Delay(1).ConfigureAwait(false);
    });
    // This is fine; the lock still works
}
```

### Solution

So if you're executing third party methods within the guarded section and if
you're concerned that they might change the current `SynchronizationContext`
then just wrap them in `Task.Run` or something similar.

## Entering the guarded section changes the current `SynchronizationContext`

Also because this lock is powered by a special `SynchronizationContext`, the
current `SynchronizationContext` will change when you call `LockAsync`. And it
switches back when you leave the guarded section. For example:

```csharp
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
```

This will have an impact in WPF or WinForms applications. For example, pretend
you have a WPF button named "Button" and this is the handler for its "Click"
event:

```csharp
partial class MyUserControl
{
    readonly ReentrantAsyncLock _asyncLock = new();

    public async void OnButtonClick(object sender, EventArgs e)
    {
        Debug.Assert(SynchronizationContext.Current is DispatcherSynchronizationContext);
        Debug.Assert(Dispatcher.CurrentDispatcher == Button.Dispatcher);
        Button.Tag = "This works";
        await using (await _asyncLock.LockAsync(CancellationToken.None))
        {
            Button.Tag = "This will not work!"; // We're no longer on the dispatcher
        }
    }
}
```

### Solution

The solution is to do the work on the dispatcher:

```csharp
partial class MyUserControl
{
    readonly ReentrantAsyncLock _asyncLock = new();

    public async void OnButtonClick(object sender, EventArgs e)
    {
        Debug.Assert(SynchronizationContext.Current is DispatcherSynchronizationContext);
        Debug.Assert(Dispatcher.CurrentDispatcher == Button.Dispatcher);
        Button.Tag = "This still works";
        await using (await _asyncLock.LockAsync(CancellationToken.None))
        {
            await Button.Dispatcher.InvokeAsync(() =>
            {
                Button.Tag = "Now this works, too!"; // We're back on the dispatcher
            });
        }
    }
}
```

# More details

[https://www.matthewathomas.com/programming/2022/06/20/introducing-reentrantasynclock.html](https://www.matthewathomas.com/programming/2022/06/20/introducing-reentrantasynclock.html)

# Release notes

|Version|Summary|
|-|-|
|0.3.x|Reduce package dependency graph|
|0.2.0|Loosen framework dependency from .Net 6 to .Net Standard 2.1|
|0.1.x|Initial release (with subsequent documentation and test changes)|