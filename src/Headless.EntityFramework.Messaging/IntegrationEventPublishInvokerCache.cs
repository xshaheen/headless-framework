// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Headless.Messaging;

namespace Headless.EntityFramework;

/// <summary>
/// Caches per-runtime-type compiled invokers that bridge a runtime-typed integration payload to the generic
/// publish on <see cref="UnitOfWorkOutbox"/>. The outbox binding exposes no non-generic publish overload, so each
/// concrete event type must be dispatched through its own closed generic method; compiling the call once per type
/// avoids per-publish reflection cost (prior art: the messaging consume pipeline).
/// </summary>
internal sealed class IntegrationEventPublishInvokerCache
{
    private readonly ConcurrentDictionary<
        Type,
        Func<UnitOfWorkOutbox, object, OutboxOptions, CancellationToken, Task>
    > _invokers = new();

    private readonly ConcurrentDictionary<
        Type,
        Func<IOutboxStoreBatch, object, OutboxOptions, CancellationToken, Task>
    > _batchInvokers = new();

    private static readonly MethodInfo _GenericBatchPublishAsync = typeof(IOutboxStoreBatch).GetMethod(
        nameof(IOutboxStoreBatch.PublishAsync),
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
    )!;

    // The three-parameter overload: the two-parameter one takes no options, and the enqueue pair belongs to the
    // queue lane. Matching on the options type rather than on the parameter count alone keeps this selection
    // unambiguous if the binding ever gains another three-parameter publish.
    private static readonly MethodInfo _GenericPublishAsync = typeof(UnitOfWorkOutbox)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(m =>
            m is { Name: nameof(UnitOfWorkOutbox.PublishAsync), IsGenericMethodDefinition: true }
            && m.GetParameters() is [_, { ParameterType: var optionsType }, _]
            && optionsType == typeof(OutboxOptions)
        );

    public Func<UnitOfWorkOutbox, object, OutboxOptions, CancellationToken, Task> GetPublishInvoker(Type eventType)
    {
        return _invokers.GetOrAdd(
            eventType,
            static type => _CreateInvoker<UnitOfWorkOutbox>(_GenericPublishAsync.MakeGenericMethod(type), type)
        );
    }

    /// <summary>The same publish, issued on a store batch opened on the unit's outbox.</summary>
    public Func<IOutboxStoreBatch, object, OutboxOptions, CancellationToken, Task> GetBatchPublishInvoker(
        Type eventType
    )
    {
        return _batchInvokers.GetOrAdd(
            eventType,
            static type => _CreateInvoker<IOutboxStoreBatch>(_GenericBatchPublishAsync.MakeGenericMethod(type), type)
        );
    }

    private static Func<TTarget, object, OutboxOptions, CancellationToken, Task> _CreateInvoker<TTarget>(
        MethodInfo publish,
        Type eventType
    )
    {
        var outbox = Expression.Parameter(typeof(TTarget), "outbox");
        var integrationEvent = Expression.Parameter(typeof(object), "integrationEvent");
        var options = Expression.Parameter(typeof(OutboxOptions), "options");
        var cancellationToken = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        // Keep concrete contract resolution while supplying the emission snapshot for each individual publish.
        var call = Expression.Call(
            outbox,
            publish,
            Expression.Convert(integrationEvent, eventType),
            options,
            cancellationToken
        );

        return Expression
            .Lambda<Func<TTarget, object, OutboxOptions, CancellationToken, Task>>(
                call,
                outbox,
                integrationEvent,
                options,
                cancellationToken
            )
            .Compile();
    }
}
