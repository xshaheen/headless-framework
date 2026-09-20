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

    // A resource-bearing unit of work current in the scope models the save pipeline having enlisted its
    // transaction (or the caller's BeginAsync(db) unit being active).
    private static IUnitOfWorkManager _ManagerWith(IUnitOfWork? current)
    {
        var manager = Substitute.For<IUnitOfWorkManager>();
        manager.Current.Returns(current);
        return manager;
    }

    private static IUnitOfWork _ResourceBearingUnitOfWork(IUnitOfWorkOutbox? outbox = null)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.Resource.Returns(Substitute.For<IUnitOfWorkResource>());
        unitOfWork.GetFeature<IUnitOfWorkOutbox>().Returns(outbox ?? new RecordingOutbox());
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
            OutboxPublishOptions? Options,
            IUnitOfWork UnitOfWork
        )> Published { get; } = [];

        public Task<PublishReceipt> PublishAsync<T>(
            IUnitOfWork unitOfWork,
            T? contentObj,
            OutboxPublishOptions? options,
            CancellationToken cancellationToken = default
        )
        {
            Published.Add((typeof(T), contentObj, options, unitOfWork));
            return Task.FromResult(new PublishReceipt(options?.MessageId ?? Guid.NewGuid().ToString(), Guid.NewGuid()));
        }

        public Task<PublishReceipt> EnqueueAsync<T>(
            IUnitOfWork unitOfWork,
            T? contentObj,
            OutboxQueueOptions? options,
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
            OutboxPublishOptions? options,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("Publish failed");
        }

        public Task<PublishReceipt> EnqueueAsync<T>(
            IUnitOfWork unitOfWork,
            T? contentObj,
            OutboxQueueOptions? options,
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
        await invoke(unitOfWork.Outbox, integrationEvent, new OutboxPublishOptions(), AbortToken);

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
        await cache.GetPublishInvoker(first.GetType())(binding, first, new OutboxPublishOptions(), AbortToken);
        await cache.GetPublishInvoker(second.GetType())(binding, second, new OutboxPublishOptions(), AbortToken);

        // then
        outbox.Published.Select(x => x.GenericType).Should().Equal(typeof(OrderPlaced), typeof(PaymentCaptured));
    }

    #endregion

    #region Dispatcher

    [Fact]
    public async Task should_be_noop_for_empty_event_list_when_dispatch_async()
    {
        // given — an empty list must short-circuit without publishing anything, and without consulting the
        // unit of work (no manager set up: Current is null).
        var outbox = new RecordingOutbox();
        var dispatcher = new OutboxIntegrationEventDispatcher(
            _ManagerWith(current: null),
            new IntegrationEventPublishInvokerCache()
        );

        // when
        await dispatcher.DispatchAsync([], AbortToken);

        // then
        outbox.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task should_publish_all_events_through_the_current_units_outbox_when_dispatch_async()
    {
        // given — the pipeline enlisted its transaction, so a resource-bearing unit is current and its outbox
        // places the rows inside that unit's transaction. The dispatcher only fans the events out to that
        // outbox; it does not touch the transaction.
        var outbox = new RecordingOutbox();
        var unitOfWork = _ResourceBearingUnitOfWork(outbox);
        var dispatcher = new OutboxIntegrationEventDispatcher(
            _ManagerWith(unitOfWork),
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<EventContext<object>> events =
        [
            new(new OrderPlaced("order-1"), "occurrence-1", "root-1", "parent-1", "tenant-1"),
            new(new PaymentCaptured("payment-1"), "occurrence-2", "root-2"),
        ];

        // when
        await dispatcher.DispatchAsync(events, AbortToken);

        // then
        outbox.Published.Should().HaveCount(2);
        outbox.Published[0].GenericType.Should().Be<OrderPlaced>();
        outbox.Published[1].GenericType.Should().Be<PaymentCaptured>();
        for (var i = 0; i < events.Count; i++)
        {
            outbox.Published[i].Payload.Should().BeSameAs(events[i].Payload);
            // Every publish enlists in the unit the manager reported as current.
            outbox.Published[i].UnitOfWork.Should().BeSameAs(unitOfWork);
            // Compare the public publish contract; record equality also includes internal transaction replay state.
            outbox
                .Published[i]
                .Options.Should()
                .BeEquivalentTo(
                    new OutboxPublishOptions
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
        var dispatcher = new OutboxIntegrationEventDispatcher(
            _ManagerWith(_ResourceBearingUnitOfWork(new ThrowingOutbox())),
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<EventContext<object>> events = [EventContext.Capture<object>(new OrderPlaced("order-1"))];

        // when
        var act = async () => await dispatcher.DispatchAsync(events, AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("Publish failed");
    }

    [Fact]
    public async Task should_propagate_cancellation_before_publishing_when_dispatch_async()
    {
        // given — a pre-cancelled token with a non-empty event list. The per-event loop trips
        // ThrowIfCancellationRequested on the first iteration, so nothing is published.
        var outbox = new RecordingOutbox();
        var dispatcher = new OutboxIntegrationEventDispatcher(
            _ManagerWith(_ResourceBearingUnitOfWork(outbox)),
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<EventContext<object>> events = [EventContext.Capture<object>(new OrderPlaced("order-1"))];
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await dispatcher.DispatchAsync(events, cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        outbox.Published.Should().BeEmpty();
    }

    [Fact]
    public void should_forward_to_dispatch_async_and_publish_all_events_when_dispatch_sync()
    {
        // given
        var outbox = new RecordingOutbox();
        var dispatcher = new OutboxIntegrationEventDispatcher(
            _ManagerWith(_ResourceBearingUnitOfWork(outbox)),
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<EventContext<object>> events = [EventContext.Capture<object>(new OrderPlaced("order-1"))];

        // when
        dispatcher.Dispatch(events);

        // then
        outbox.Published.Should().ContainSingle();
        outbox.Published[0].GenericType.Should().Be<OrderPlaced>();
    }

    [Fact]
    public async Task should_fail_loud_when_dispatch_async_without_a_unit_of_work()
    {
        // given — integration events emitted while saving inside a caller-managed transaction that no unit of
        // work owns: dispatching would be non-atomic. The dispatcher must fail loud, naming the remedy, instead
        // of shipping a message a caller rollback can no longer recall.
        var dispatcher = new OutboxIntegrationEventDispatcher(
            _ManagerWith(current: null),
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<EventContext<object>> events = [EventContext.Capture<object>(new OrderPlaced("order-1"))];

        // when
        var act = async () => await dispatcher.DispatchAsync(events, AbortToken);

        // then — fails loud and publishes nothing
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IUnitOfWorkManager.BeginAsync(db)*");
    }

    [Fact]
    public async Task should_fail_loud_when_dispatch_async_under_a_resource_less_unit_of_work()
    {
        // given — a resource-less unit (BeginAsync() with no db) coordinates nothing transactional, so an outbox
        // write under it would still be autonomous; the guard is on the resource, not on the unit's presence.
        var dispatcher = new OutboxIntegrationEventDispatcher(
            _ManagerWith(_ResourceLessUnitOfWork()),
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<EventContext<object>> events = [EventContext.Capture<object>(new OrderPlaced("order-1"))];

        // when
        var act = async () => await dispatcher.DispatchAsync(events, AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IUnitOfWorkManager.BeginAsync(db)*");
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
        services.Single(d => d.ServiceType == typeof(IUnitOfWorkManager)).Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion
}
