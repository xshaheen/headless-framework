// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Headless.Domain;
using Headless.Messaging;

namespace Headless.EntityFramework;

/// <summary>
/// Caches per-runtime-type compiled invokers that bridge a non-generic <see cref="IIntegrationEvent"/> to the
/// generic <see cref="IOutboxBus.PublishAsync{T}"/>. <see cref="IOutboxBus"/> exposes no non-generic publish
/// overload, so each concrete event type must be dispatched through its own closed generic method; compiling
/// the call once per type avoids per-publish reflection cost (prior art: the messaging consume pipeline).
/// </summary>
internal sealed class IntegrationEventPublishInvokerCache
{
    private readonly ConcurrentDictionary<
        Type,
        Func<IOutboxBus, IIntegrationEvent, CancellationToken, Task>
    > _invokers = new();

    private static readonly MethodInfo _GenericPublishAsync = typeof(IOutboxBus)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(m => m is { Name: nameof(IOutboxBus.PublishAsync), IsGenericMethodDefinition: true });

    public Func<IOutboxBus, IIntegrationEvent, CancellationToken, Task> GetPublishInvoker(Type eventType)
    {
        return _invokers.GetOrAdd(eventType, _CreateInvoker);
    }

    private static Func<IOutboxBus, IIntegrationEvent, CancellationToken, Task> _CreateInvoker(Type eventType)
    {
        var bus = Expression.Parameter(typeof(IOutboxBus), "bus");
        var integrationEvent = Expression.Parameter(typeof(IIntegrationEvent), "integrationEvent");
        var cancellationToken = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        // bus.PublishAsync<TConcrete>((TConcrete)integrationEvent, options: null, cancellationToken)
        var call = Expression.Call(
            bus,
            _GenericPublishAsync.MakeGenericMethod(eventType),
            Expression.Convert(integrationEvent, eventType),
            Expression.Constant(null, typeof(PublishOptions)),
            cancellationToken
        );

        return Expression
            .Lambda<Func<IOutboxBus, IIntegrationEvent, CancellationToken, Task>>(
                call,
                bus,
                integrationEvent,
                cancellationToken
            )
            .Compile();
    }
}
