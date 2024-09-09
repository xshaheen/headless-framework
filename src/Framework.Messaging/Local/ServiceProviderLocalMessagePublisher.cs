using System.Collections.Concurrent;
using System.Reflection;
using Framework.Kernel.Domains;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Framework.Messaging;

public sealed class ServiceProviderLocalMessagePublisher(IServiceProvider services) : ILocalMessagePublisher
{
    private static readonly ConcurrentDictionary<Type, int> _HandlerOrderCache = new();

    public void Publish<T>(T message)
        where T : class, ILocalMessage
    {
        var handlers = services.GetServices<ILocalMessageHandler<T>>();
        var exceptions = new List<Exception>();

        foreach (var handler in handlers.OrderBy(handler => _GetHandlerOrder(handler.GetType())))
        {
            try
            {
                handler.HandleAsync(message).WaitAndUnwrapException();
            }
            catch (TargetInvocationException e)
            {
                exceptions.Add(e.InnerException!);
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }
        }

        if (exceptions.Count > 0)
        {
            _ThrowOriginalExceptions(typeof(T), exceptions);
        }
    }

    public async Task PublishAsync<T>(T message, CancellationToken abortToken = default)
        where T : class, ILocalMessage
    {
        var handlers = services.GetServices<ILocalMessageHandler<T>>();
        var exceptions = new List<Exception>();

        foreach (var handler in handlers.OrderBy(handler => _GetHandlerOrder(handler.GetType())))
        {
            try
            {
                await handler.HandleAsync(message, abortToken);
            }
            catch (TargetInvocationException e)
            {
                exceptions.Add(e.InnerException!);
            }
            catch (Exception e)
            {
                exceptions.Add(e);
            }
        }

        if (exceptions.Count > 0)
        {
            _ThrowOriginalExceptions(typeof(T), exceptions);
        }
    }

    #region Helpers

    private static int _GetHandlerOrder(Type handlerType)
    {
        return _HandlerOrderCache.GetOrAdd(
            handlerType,
            type =>
            {
                var attribute = type.GetCustomAttribute<LocalEventHandlerOrderAttribute>();
                return attribute?.Order ?? 0;
            }
        );
    }

    private static void _ThrowOriginalExceptions(Type eventType, List<Exception> exceptions)
    {
        if (exceptions.Count == 1)
        {
            exceptions[0].ReThrow();
        }

        throw new AggregateException(
            "More than one error has occurred while triggering the event: " + eventType,
            exceptions
        );
    }

    #endregion
}
