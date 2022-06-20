// ReSharper disable UnusedMemberInSuper.Global
namespace ReentrantAsyncLock;

using System.Runtime.CompilerServices;

interface IAwaiter<out T> : INotifyCompletion
{
    bool IsCompleted { get; }

    T GetResult();
}