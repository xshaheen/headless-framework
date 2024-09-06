using Framework.Kernel.Checks;

namespace Framework.Kernel.BuildingBlocks.Helpers.System;

public sealed class AsyncEvent<TEventArgs>(bool parallelInvoke = false) : IObservable<TEventArgs>, IDisposable
    where TEventArgs : EventArgs
{
    private readonly List<Func<object, TEventArgs, Task>> _invocationList = [];
    private readonly object _lockObject = new();

    // ReSharper disable once InconsistentlySynchronizedField
    public bool HasHandlers => _invocationList.Count > 0;

    public IDisposable AddHandler(Func<object, TEventArgs, Task> callback)
    {
        Argument.IsNotNull(callback);

        lock (_lockObject)
        {
            _invocationList.Add(callback);
        }

        return new EventHandlerDisposable<TEventArgs>(this, callback);
    }

    public IDisposable AddSyncHandler(Action<object, TEventArgs> callback)
    {
        return AddHandler(
            (sender, args) =>
            {
                callback(sender, args);
                return Task.CompletedTask;
            }
        );
    }

    public void RemoveHandler(Func<object, TEventArgs, Task> callback)
    {
        Argument.IsNotNull(callback);

        lock (_lockObject)
        {
            _invocationList.Remove(callback);
        }
    }

    public async Task InvokeAsync(object sender, TEventArgs eventArgs)
    {
        List<Func<object, TEventArgs, Task>> tmpInvocationList;

        lock (_lockObject)
        {
            tmpInvocationList = [.. _invocationList];
        }

        if (parallelInvoke)
        {
            await Task.WhenAll(tmpInvocationList.Select(callback => callback(sender, eventArgs))).AnyContext();
        }
        else
        {
            foreach (var callback in tmpInvocationList)
            {
                await callback(sender, eventArgs).AnyContext();
            }
        }
    }

    public IDisposable Subscribe(IObserver<TEventArgs> observer)
    {
        return AddSyncHandler((_, args) => observer.OnNext(args));
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
            _invocationList.Clear();
        }
    }

    private sealed class EventHandlerDisposable<T>(AsyncEvent<T> @event, Func<object, T, Task> callback) : IDisposable
        where T : EventArgs
    {
        public void Dispose()
        {
            @event.RemoveHandler(callback);
        }
    }
}
