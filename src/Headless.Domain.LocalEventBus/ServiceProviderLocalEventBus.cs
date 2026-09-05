// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Domain;

internal sealed class ServiceProviderLocalEventBus(IServiceProvider services) : ILocalEventBus
{
    private static readonly ConditionalWeakTable<Type, StrongBox<int>> _HandlerOrderCache = [];

    private static readonly ConditionalWeakTable<Type, StrongBox<int>>.CreateValueCallback _ComputeHandlerOrder =
        static type =>
        {
            var attribute = type.GetCustomAttribute<DomainEventHandlerOrderAttribute>();
            return new StrongBox<int>(attribute?.Order ?? 0);
        };

    public ValueTask PublishAsync<T>(T domainEvent, CancellationToken cancellationToken = default)
        where T : class, IDomainEvent => PublishAsync((IDomainEvent)domainEvent, cancellationToken);

    public ValueTask PublishAsync<T>(EventOccurrence<T> occurrence, CancellationToken cancellationToken = default)
        where T : class, IDomainEvent
    {
        Argument.IsNotNull(occurrence);
        return PublishAsync(
            new EventOccurrence<IDomainEvent>(occurrence.Payload, occurrence.Context),
            cancellationToken
        );
    }

    private async ValueTask _PublishAsync<T>(
        T domainEvent,
        EventOccurrenceContext context,
        CancellationToken cancellationToken
    )
        where T : class, IDomainEvent
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
                await handler.HandleAsync(domainEvent, context, cancellationToken).ConfigureAwait(false);
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

    // The non-generic overload dispatches to the exact runtime type (no contravariant base-type traversal),
    // matching the generic path. A compiled per-type invoker avoids per-call reflection cost.
    public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(domainEvent);

        return PublishAsync(EventOccurrence.Capture<IDomainEvent>(domainEvent), cancellationToken);
    }

    public ValueTask PublishAsync(
        EventOccurrence<IDomainEvent> occurrence,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(occurrence);
        Argument.IsNotNull(occurrence.Payload);
        var invoker = _AsyncInvokers.GetOrAdd(occurrence.Payload.GetType(), _CreateAsyncInvoker);
        return invoker(this, occurrence.Payload, occurrence.Context, cancellationToken);
    }

    #region Helpers

    private static int _GetHandlerOrder(Type handlerType)
    {
        return _HandlerOrderCache.GetValue(handlerType, _ComputeHandlerOrder).Value;
    }

    private static IDomainEventHandler<T>[] _OrderHandlers<T>(IEnumerable<IDomainEventHandler<T>> handlers)
        where T : class, IDomainEvent
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
        // The generic Publish<T>/PublishAsync<T> constrain `T : class`; a value-type event would make
        // MakeGenericMethod throw a cryptic ArgumentException. Fail fast with an actionable message instead.
        if (eventType.IsValueType)
        {
            throw new ArgumentException(
                $"Domain event type '{eventType}' must be a reference type; the generic publish path constrains 'T : class'.",
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
        Func<ServiceProviderLocalEventBus, IDomainEvent, EventOccurrenceContext, CancellationToken, ValueTask>
    > _AsyncInvokers = new();

    private static readonly MethodInfo _GenericPublishAsync = typeof(ServiceProviderLocalEventBus)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
        .Single(m => m is { Name: nameof(_PublishAsync), IsGenericMethodDefinition: true });

    private static Func<
        ServiceProviderLocalEventBus,
        IDomainEvent,
        EventOccurrenceContext,
        CancellationToken,
        ValueTask
    > _CreateAsyncInvoker(Type eventType)
    {
        _EnsureReferenceType(eventType);

        var self = Expression.Parameter(typeof(ServiceProviderLocalEventBus), "self");
        var domainEvent = Expression.Parameter(typeof(IDomainEvent), "domainEvent");
        var context = Expression.Parameter(typeof(EventOccurrenceContext), "context");
        var cancellationToken = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var call = Expression.Call(
            self,
            _GenericPublishAsync.MakeGenericMethod(eventType),
            Expression.Convert(domainEvent, eventType),
            context,
            cancellationToken
        );

        return Expression
            .Lambda<
                Func<ServiceProviderLocalEventBus, IDomainEvent, EventOccurrenceContext, CancellationToken, ValueTask>
            >(call, self, domainEvent, context, cancellationToken)
            .Compile();
    }

    #endregion
}
