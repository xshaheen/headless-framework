// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Domain;

internal sealed class ServiceProviderDomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private static readonly ConditionalWeakTable<Type, StrongBox<int>> _HandlerOrderCache = [];

    private static readonly ConditionalWeakTable<Type, StrongBox<int>>.CreateValueCallback _ComputeHandlerOrder =
        static type =>
        {
            var attribute = type.GetCustomAttribute<DomainEventHandlerOrderAttribute>();
            return new StrongBox<int>(attribute?.Order ?? 0);
        };

    public ValueTask DispatchAsync<TPayload>(
        EventContext<TPayload> context,
        CancellationToken cancellationToken = default
    )
        where TPayload : class
    {
        Argument.IsNotNull(context);
        var payloadType = context.Payload.GetType();
        if (payloadType == typeof(TPayload))
        {
            return _DispatchAsync(context, cancellationToken);
        }

        var invoker = _AsyncInvokers.GetOrAdd(payloadType, _CreateAsyncInvoker);
        var untyped =
            context as EventContext<object>
            ?? new(context.Payload, context.EventId, context.CorrelationId, context.CausationId, context.TenantId);
        return invoker(this, untyped, cancellationToken);
    }

    private async ValueTask _DispatchAsync<T>(EventContext<T> context, CancellationToken cancellationToken)
        where T : class
    {
        using var emissionScope = EventEmissionScope.Begin(context);
        var handlers = services.GetServices<IDomainEventHandler<T>>();
        List<Exception>? exceptions = null;

        foreach (var handler in _OrderHandlers(handlers))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (exceptions is { Count: > 0 })
                {
                    _ThrowOriginalExceptions(typeof(T), exceptions);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            try
            {
                await handler.HandleAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (TargetInvocationException e)
            {
                // A handler that itself threw a TargetInvocationException is unwrapped to its inner
                // exception; fall back to the wrapper when the inner is null (defensive — InnerException is nullable).
                (exceptions ??= []).Add(e.InnerException ?? e);
            }
            catch (Exception e)
            {
                (exceptions ??= []).Add(e);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            _ThrowOriginalExceptions(typeof(T), exceptions);
        }
    }

    #region Helpers

    private static int _GetHandlerOrder(Type handlerType)
    {
        return _HandlerOrderCache.GetValue(handlerType, _ComputeHandlerOrder).Value;
    }

    private static IDomainEventHandler<T>[] _OrderHandlers<T>(IEnumerable<IDomainEventHandler<T>> handlers)
        where T : class
    {
        // Return a concrete array so the foreach call sites iterate it with the array enumerator (no heap
        // IEnumerator allocation). OrderBy is a stable sort but allocates a buffer on every publish, so skip
        // it for the common 0/1-handler case and for multi-handler sets where every handler keeps the default
        // order (registration order wins).
        var array = handlers.AsArray();

        if (array.Length <= 1)
        {
            return array;
        }

        foreach (var handler in array)
        {
            if (_GetHandlerOrder(handler.GetType()) != 0)
            {
                return [.. array.OrderBy(ordered => _GetHandlerOrder(ordered.GetType()))];
            }
        }

        return array;
    }

    private static void _EnsureReferenceType(Type eventType)
    {
        // The generic DispatchAsync<T> constrains `T : class`; a value-type event would make
        // MakeGenericMethod throw a cryptic ArgumentException. Fail fast with an actionable message instead.
        if (eventType.IsValueType)
        {
            throw new ArgumentException(
                $"Domain event type '{eventType}' must be a reference type; the generic dispatch path constrains 'T : class'.",
                nameof(eventType)
            );
        }
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

    #region Runtime-typed invoker cache

    private static readonly ConcurrentDictionary<
        Type,
        Func<ServiceProviderDomainEventDispatcher, EventContext<object>, CancellationToken, ValueTask>
    > _AsyncInvokers = new();

    private static readonly MethodInfo _GenericDispatchAsync = typeof(ServiceProviderDomainEventDispatcher).GetMethod(
        nameof(_DispatchRuntimeAsync),
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
    )!;

    // The batch envelope may be object-typed; rebuilding only its generic view preserves the captured identity.
    private ValueTask _DispatchRuntimeAsync<T>(EventContext<object> context, CancellationToken cancellationToken)
        where T : class =>
        _DispatchAsync(
            new EventContext<T>(
                (T)context.Payload,
                context.EventId,
                context.CorrelationId,
                context.CausationId,
                context.TenantId
            ),
            cancellationToken
        );

    private static Func<
        ServiceProviderDomainEventDispatcher,
        EventContext<object>,
        CancellationToken,
        ValueTask
    > _CreateAsyncInvoker(Type eventType)
    {
        _EnsureReferenceType(eventType);

        var self = Expression.Parameter(typeof(ServiceProviderDomainEventDispatcher), "self");
        var context = Expression.Parameter(typeof(EventContext<object>), "context");
        var cancellationToken = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var call = Expression.Call(
            self,
            _GenericDispatchAsync.MakeGenericMethod(eventType),
            context,
            cancellationToken
        );

        return Expression
            .Lambda<Func<ServiceProviderDomainEventDispatcher, EventContext<object>, CancellationToken, ValueTask>>(
                call,
                self,
                context,
                cancellationToken
            )
            .Compile();
    }

    #endregion
}
