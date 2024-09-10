using Framework.Kernel.BuildingBlocks.Helpers.System;

namespace Framework.Queueing.Internals;

public sealed class FoundatioAsyncEventAdapter<TFoundatioEvent, TFrameworkEvent>(
    Foundatio.Utility.AsyncEvent<TFoundatioEvent> asyncEvent,
    Func<TFoundatioEvent, TFrameworkEvent> foundationEventMapper,
    Func<TFrameworkEvent, TFoundatioEvent> frameworkEventMapper
) : IAsyncEvent<TFrameworkEvent>
    where TFoundatioEvent : EventArgs
    where TFrameworkEvent : EventArgs
{
    public bool ParallelInvoke => true;

    public bool HasHandlers => asyncEvent.HasHandlers;

    public IDisposable AddHandler(Func<object, TFrameworkEvent, Task> callback)
    {
        return asyncEvent.AddHandler(async (sender, args) => await callback(sender, foundationEventMapper(args)));
    }

    public IDisposable AddHandler(Action<object, TFrameworkEvent> callback)
    {
        return asyncEvent.AddSyncHandler((sender, args) => callback(sender, foundationEventMapper(args)));
    }

    public void RemoveHandler(Func<object, TFrameworkEvent, Task> callback)
    {
        asyncEvent.RemoveHandler((sender, args) => callback(sender, foundationEventMapper(args)));
    }

    public Task InvokeAsync(object sender, TFrameworkEvent eventArgs)
    {
        return asyncEvent.InvokeAsync(sender, frameworkEventMapper(eventArgs));
    }

    public void ClearHandlers()
    {
        asyncEvent.Dispose();
    }

    public IDisposable Subscribe(IObserver<TFrameworkEvent> observer)
    {
        return asyncEvent.Subscribe(args => observer.OnNext(foundationEventMapper(args)));
    }
}
