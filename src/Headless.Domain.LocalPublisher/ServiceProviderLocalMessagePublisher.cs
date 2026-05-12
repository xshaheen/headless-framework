// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx.Synchronous;

namespace Headless.Domain;

public sealed class ServiceProviderLocalMessagePublisher(IServiceProvider services) : ILocalMessagePublisher
{
    private readonly ConditionalWeakTable<Type, StrongBox<int>> _handlerOrderCache = new();

    private static readonly ConditionalWeakTable<Type, StrongBox<int>>.CreateValueCallback _ComputeHandlerOrder =
        static type =>
        {
            var attribute = type.GetCustomAttribute<LocalEventHandlerOrderAttribute>();
            return new StrongBox<int>(attribute?.Order ?? 0);
        };

    public void Publish<T>(T message)
        where T : class, ILocalMessage
    {
        var handlers = services.GetServices<ILocalMessageHandler<T>>();
        var exceptions = new List<Exception>();

        foreach (var handler in handlers.OrderBy(handler => _GetHandlerOrder(handler.GetType())))
        {
            try
            {
                handler.HandleAsync(message).AsTask().WaitAndUnwrapException();
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

    public async ValueTask PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class, ILocalMessage
    {
        var handlers = services.GetServices<ILocalMessageHandler<T>>();
        var exceptions = new List<Exception>();

        foreach (var handler in handlers.OrderBy(handler => _GetHandlerOrder(handler.GetType())))
        {
            try
            {
                await handler.HandleAsync(message, cancellationToken);
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

    private int _GetHandlerOrder(Type handlerType)
    {
        return _handlerOrderCache.GetValue(handlerType, _ComputeHandlerOrder).Value;
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
