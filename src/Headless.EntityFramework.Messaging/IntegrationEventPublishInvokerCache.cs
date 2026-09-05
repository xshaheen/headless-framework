// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Headless.Messaging;

namespace Headless.EntityFramework;

/// <summary>
/// Caches per-runtime-type compiled invokers that bridge a runtime-typed integration payload to the
/// generic <see cref="IBus.PublishAsync{T}"/>. <see cref="IBus"/> exposes no non-generic publish
/// overload, so each concrete event type must be dispatched through its own closed generic method; compiling
/// the call once per type avoids per-publish reflection cost (prior art: the messaging consume pipeline).
/// </summary>
internal sealed class IntegrationEventPublishInvokerCache
{
    private readonly ConcurrentDictionary<Type, Func<IBus, object, PublishOptions, CancellationToken, Task>> _invokers =
        new();

    private static readonly MethodInfo _GenericPublishAsync = typeof(IBus)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(m => m is { Name: nameof(IBus.PublishAsync), IsGenericMethodDefinition: true });

    public Func<IBus, object, PublishOptions, CancellationToken, Task> GetPublishInvoker(Type eventType)
    {
        return _invokers.GetOrAdd(eventType, _CreateInvoker);
    }

    private static Func<IBus, object, PublishOptions, CancellationToken, Task> _CreateInvoker(Type eventType)
    {
        var bus = Expression.Parameter(typeof(IBus), "bus");
        var integrationEvent = Expression.Parameter(typeof(object), "integrationEvent");
        var options = Expression.Parameter(typeof(PublishOptions), "options");
        var cancellationToken = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        // Keep concrete contract resolution while supplying the emission snapshot for each individual publish.
        var call = Expression.Call(
            bus,
            _GenericPublishAsync.MakeGenericMethod(eventType),
            Expression.Convert(integrationEvent, eventType),
            options,
            cancellationToken
        );

        return Expression
            .Lambda<Func<IBus, object, PublishOptions, CancellationToken, Task>>(
                call,
                bus,
                integrationEvent,
                options,
                cancellationToken
            )
            .Compile();
    }
}
