// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Messaging;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class OutboxIntegrationEventDispatcherTests : TestBase
{
    #region Test Infrastructure

    private sealed record OrderPlaced(string UniqueId);

    private sealed record PaymentCaptured(string UniqueId);

    // A resource-bearing unit models the save pipeline having enlisted its transaction (or the caller's
    // BeginAsync(db) unit) and handed it to the dispatcher.
    private static IUnitOfWork _ResourceBearingUnitOfWork(IUnitOfWorkOutbox? outbox = null)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.Resource.Returns(Substitute.For<IUnitOfWorkResource>());
        unitOfWork.GetFeature<IUnitOfWorkOutbox>().Returns(outbox ?? new RecordingOutbox());
        // unit.Outbox keeps its binding as unit-local state through GetOrAdd; the substitute must honour the
        // create-once contract or the accessor hands back null.
        var state = new Dictionary<Type, object>();
        unitOfWork
            .GetOrAdd(Arg.Any<IUnitOfWork>(), Arg.Any<Func<IUnitOfWork, IUnitOfWork, UnitOfWorkOutbox>>())
            .Returns(call =>
            {
                if (!state.TryGetValue(typeof(UnitOfWorkOutbox), out var existing))
                {
                    existing = call.ArgAt<Func<IUnitOfWork, IUnitOfWork, UnitOfWorkOutbox>>(1)(unitOfWork, unitOfWork);
                    state[typeof(UnitOfWorkOutbox)] = existing;
                }

                return (UnitOfWorkOutbox)existing;
            });
        return unitOfWork;
    }

    private static IUnitOfWork _ResourceLessUnitOfWork()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.Resource.Returns((IUnitOfWorkResource?)null);
        return unitOfWork;
    }

    private sealed class RecordingOutbox : IUnitOfWorkOutbox
    {
        public List<(
            Type GenericType,
            object? Payload,
            OutboxOptions? Options,
            IUnitOfWork UnitOfWork
        )> Published { get; } = [];

        public Task<PublishReceipt> PublishAsync<T>(
            IUnitOfWork unitOfWork,
            T? contentObj,
            OutboxOptions? options,
            CancellationToken cancellationToken = default
        )
        {
            Published.Add((typeof(T), contentObj, options, unitOfWork));
            return Task.FromResult(new PublishReceipt(options?.MessageId ?? Guid.NewGuid().ToString(), Guid.NewGuid()));
        }

        public Task<PublishReceipt> EnqueueAsync<T>(
            IUnitOfWork unitOfWork,
            T? contentObj,
            OutboxOptions? options,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException("Integration events take the bus lane.");
        }
    }

    private sealed class ThrowingOutbox : IUnitOfWorkOutbox
    {
        public Task<PublishReceipt> PublishAsync<T>(
            IUnitOfWork unitOfWork,
            T? contentObj,
            OutboxOptions? options,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("Publish failed");
        }

        public Task<PublishReceipt> EnqueueAsync<T>(
            IUnitOfWork unitOfWork,
            T? contentObj,
            OutboxOptions? options,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("Publish failed");
        }
    }

    #endregion

    #region Invoker cache

    [Fact]
    public async Task should_publish_runtime_typed_event_through_its_concrete_generic_overload_when_invoker()
    {
        // given — the event is held as object; the invoker must route to the outbox's PublishAsync<OrderPlaced>,
        // not PublishAsync<object>, recovering the concrete type from the runtime instance.
        var cache = new IntegrationEventPublishInvokerCache();
        var outbox = new RecordingOutbox();
        var unitOfWork = _ResourceBearingUnitOfWork(outbox);
        object integrationEvent = new OrderPlaced("order-1");

        // when
        var invoke = cache.GetPublishInvoker(integrationEvent.GetType());
        await invoke(unitOfWork.Outbox, integrationEvent, new OutboxOptions(), AbortToken);

        // then — the concrete generic overload ran, and the binding carried the publishing handle with it
        outbox.Published.Should().ContainSingle();
        outbox.Published[0].GenericType.Should().Be<OrderPlaced>();
        outbox.Published[0].Payload.Should().BeSameAs(integrationEvent);
        outbox.Published[0].UnitOfWork.Should().BeSameAs(unitOfWork);
    }

    [Fact]
    public void should_be_cached_per_event_type_when_invoker()
    {
        // given
        var cache = new IntegrationEventPublishInvokerCache();

        // when
        var first = cache.GetPublishInvoker(typeof(OrderPlaced));
        var second = cache.GetPublishInvoker(typeof(OrderPlaced));

        // then
        first.Should().BeSameAs(second);
    }

    [Fact]
    public async Task should_route_each_concrete_type_to_its_own_generic_overload_when_invoker()
    {
        // given
        var cache = new IntegrationEventPublishInvokerCache();
        var outbox = new RecordingOutbox();
        var binding = _ResourceBearingUnitOfWork(outbox).Outbox;
        object first = new OrderPlaced("order");
        object second = new PaymentCaptured("payment");

        // when
        await cache.GetPublishInvoker(first.GetType())(binding, first, new OutboxOptions(), AbortToken);
        await cache.GetPublishInvoker(second.GetType())(binding, second, new OutboxOptions(), AbortToken);

        // then
        outbox.Published.Select(x => x.GenericType).Should().Equal(typeof(OrderPlaced), typeof(PaymentCaptured));
    }

    #endregion

    #region Dispatcher

    [Fact]
    public async Task should_be_noop_for_empty_event_list_when_dispatch_async()
    {
        // given — an empty list must short-circuit without publishing anything, and without consulting the
        // unit's resource: a resource-less unit would otherwise be refused.
        var outbox = new RecordingOutbox();
        var unitOfWork = _ResourceLessUnitOfWork();
        var dispatcher = new OutboxIntegrationEventDispatcher(new IntegrationEventPublishInvokerCache());

        // when
        await dispatcher.DispatchAsync(unitOfWork, [], AbortToken);

        // then
        outbox.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task should_publish_all_events_through_the_given_units_outbox_when_dispatch_async()
    {
        // given — the pipeline enlisted its transaction and handed over a resource-bearing unit whose outbox
        // places the rows inside that unit's transaction. The dispatcher only fans the events out to that
        // outbox; it does not touch the transaction.
        var outbox = new RecordingOutbox();
        var unitOfWork = _ResourceBearingUnitOfWork(outbox);
        var dispatcher = new OutboxIntegrationEventDispatcher(new IntegrationEventPublishInvokerCache());
        IReadOnlyList<EventContext<object>> events =
        [
            new(new OrderPlaced("order-1"), "occurrence-1", "root-1", "parent-1", "tenant-1"),
            new(new PaymentCaptured("payment-1"), "occurrence-2", "root-2"),
        ];

        // when
        await dispatcher.DispatchAsync(unitOfWork, events, AbortToken);

        // then
        outbox.Published.Should().HaveCount(2);
        outbox.Published[0].GenericType.Should().Be<OrderPlaced>();
        outbox.Published[1].GenericType.Should().Be<PaymentCaptured>();
        for (var i = 0; i < events.Count; i++)
        {
            outbox.Published[i].Payload.Should().BeSameAs(events[i].Payload);
            // Every publish enlists in the unit the pipeline handed over.
            outbox.Published[i].UnitOfWork.Should().BeSameAs(unitOfWork);
            // Compare the public publish contract; record equality also includes internal transaction replay state.
            outbox
                .Published[i]
                .Options.Should()
                .BeEquivalentTo(
                    new OutboxOptions
                    {
                        MessageId = events[i].EventId,
                        CorrelationId = events[i].CorrelationId,
                        CausationId = events[i].CausationId,
                        TenantId = events[i].TenantId,
                        SuppressAmbientBusinessContext = true,
                    }
                );
        }
    }

    [Fact]
    public async Task should_propagate_publish_failure_when_dispatch_async()
    {
        // given
        var unitOfWork = _ResourceBearingUnitOfWork(new ThrowingOutbox());
        var dispatcher = new OutboxIntegrationEventDispatcher(new IntegrationEventPublishInvokerCache());
        IReadOnlyList<EventContext<object>> events = [EventContext.Capture<object>(new OrderPlaced("order-1"))];

        // when
        var act = async () => await dispatcher.DispatchAsync(unitOfWork, events, AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("Publish failed");
    }

    [Fact]
    public async Task should_propagate_cancellation_before_publishing_when_dispatch_async()
    {
        // given — a pre-cancelled token with a non-empty event list. The per-event loop trips
        // ThrowIfCancellationRequested on the first iteration, so nothing is published.
        var outbox = new RecordingOutbox();
        var unitOfWork = _ResourceBearingUnitOfWork(outbox);
        var dispatcher = new OutboxIntegrationEventDispatcher(new IntegrationEventPublishInvokerCache());
        IReadOnlyList<EventContext<object>> events = [EventContext.Capture<object>(new OrderPlaced("order-1"))];
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await dispatcher.DispatchAsync(unitOfWork, events, cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        outbox.Published.Should().BeEmpty();
    }

    [Fact]
    public void should_forward_to_dispatch_async_and_publish_all_events_when_dispatch_sync()
    {
        // given
        var outbox = new RecordingOutbox();
        var unitOfWork = _ResourceBearingUnitOfWork(outbox);
        var dispatcher = new OutboxIntegrationEventDispatcher(new IntegrationEventPublishInvokerCache());
        IReadOnlyList<EventContext<object>> events = [EventContext.Capture<object>(new OrderPlaced("order-1"))];

        // when
        dispatcher.Dispatch(unitOfWork, events);

        // then
        outbox.Published.Should().ContainSingle();
        outbox.Published[0].GenericType.Should().Be<OrderPlaced>();
    }

    [Fact]
    public async Task should_reject_a_null_unit_of_work_when_dispatch_async()
    {
        // given — the unit is a required argument now that nothing ambient can stand in for it; the save
        // pipeline refuses a caller-managed transaction without a unit before it ever reaches the dispatcher.
        var dispatcher = new OutboxIntegrationEventDispatcher(new IntegrationEventPublishInvokerCache());
        IReadOnlyList<EventContext<object>> events = [EventContext.Capture<object>(new OrderPlaced("order-1"))];

        // when
        var act = async () => await dispatcher.DispatchAsync(null!, events, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_fail_loud_when_dispatch_async_under_a_resource_less_unit_of_work()
    {
        // given — a resource-less unit (BeginAsync() with no db) coordinates nothing transactional, so an outbox
        // write under it would still be autonomous; the guard is on the resource, not on the unit's presence.
        var unitOfWork = _ResourceLessUnitOfWork();
        var dispatcher = new OutboxIntegrationEventDispatcher(new IntegrationEventPublishInvokerCache());
        IReadOnlyList<EventContext<object>> events = [EventContext.Capture<object>(new OrderPlaced("order-1"))];

        // when
        var act = async () => await dispatcher.DispatchAsync(unitOfWork, events, AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IUnitOfWorkFactory.BeginAsync(db)*");
    }

    #endregion

    #region Setup

    [Fact]
    public void should_register_dispatcher_scoped_and_return_builder_when_add_integration_event_outbox()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddHeadlessDbContextServices();

        // when
        var returned = builder.AddIntegrationEventOutbox();

        // then — chains, and registers the outbox dispatcher (TryAdd, scoped)
        returned.Should().BeSameAs(builder);
        services
            .Should()
            .ContainSingle(d =>
                d.ServiceType == typeof(IHeadlessOutboxDispatcher)
                && d.ImplementationType == typeof(OutboxIntegrationEventDispatcher)
                && d.Lifetime == ServiceLifetime.Scoped
            );
    }

    [Fact]
    public void should_be_idempotent_when_add_integration_event_outbox()
    {
        // given
        var services = new ServiceCollection();
        var builder = services.AddHeadlessDbContextServices();

        // when — repeated calls must not register duplicate dispatcher descriptors (TryAdd)
        builder.AddIntegrationEventOutbox();
        builder.AddIntegrationEventOutbox();

        // then
        services.Count(d => d.ServiceType == typeof(IHeadlessOutboxDispatcher)).Should().Be(1);
    }

    [Fact]
    public void should_resolve_the_dispatcher_against_the_scoped_unit_of_work_manager()
    {
        // given — the manager the dispatcher consults is the scoped one AddHeadlessDbContextServices registers;
        // no separate registration is needed, and resolving the dispatcher from a scope works under validation.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContextServices().AddIntegrationEventOutbox();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // when
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IHeadlessOutboxDispatcher>();

        // then
        dispatcher.Should().BeOfType<OutboxIntegrationEventDispatcher>();
        services
            .Single(d => d.ServiceType == typeof(IUnitOfWorkFactory))
            .Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
    }

    #endregion
}
