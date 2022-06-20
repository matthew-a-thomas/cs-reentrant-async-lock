// ReSharper disable UnusedMemberInSuper.Global
namespace ReentrantAsyncLock;

interface IAwaitable<T, out TAwaiter>
where TAwaiter : IAwaiter<T>
{
    TAwaiter GetAwaiter();
}