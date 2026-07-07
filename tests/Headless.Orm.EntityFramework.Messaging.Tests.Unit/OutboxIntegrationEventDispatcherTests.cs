// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class OutboxIntegrationEventDispatcherTests : TestBase
{
    #region Test Infrastructure

    private sealed record OrderPlaced(string UniqueId) : IIntegrationEvent;

    private sealed record PaymentCaptured(string UniqueId) : IIntegrationEvent;

    // An ambient coordinator (Current != null) models the save pipeline having opened a coordinated transaction.
    private static ICurrentCommitCoordinator _AmbientCoordinator()
    {
        var current = Substitute.For<ICurrentCommitCoordinator>();
        current.Current.Returns(Substitute.For<ICommitCoordinator>());
        return current;
    }

    private sealed class RecordingOutboxBus : IOutboxBus
    {
        public List<(Type GenericType, object? Payload)> Published { get; } = [];

        public Task PublishAsync<T>(
            T? contentObj,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Published.Add((typeof(T), contentObj));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingOutboxBus : IOutboxBus
    {
        public Task PublishAsync<T>(
            T? contentObj,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("Publish failed");
        }
    }

    #endregion

    #region Invoker cache

    [Fact]
    public async Task invoker_should_publish_runtime_typed_event_through_its_concrete_generic_overload()
    {
        // given — the event is held as IIntegrationEvent; the invoker must route to PublishAsync<OrderPlaced>,
        // not PublishAsync<IIntegrationEvent>, recovering the concrete type from the runtime instance.
        var cache = new IntegrationEventPublishInvokerCache();
        var bus = new RecordingOutboxBus();
        IIntegrationEvent integrationEvent = new OrderPlaced("order-1");

        // when
        var invoke = cache.GetPublishInvoker(integrationEvent.GetType());
        await invoke(bus, integrationEvent, AbortToken);

        // then
        bus.Published.Should().ContainSingle();
        bus.Published[0].GenericType.Should().Be<OrderPlaced>();
        bus.Published[0].Payload.Should().BeSameAs(integrationEvent);
    }

    [Fact]
    public void invoker_should_be_cached_per_event_type()
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
    public async Task invoker_should_route_each_concrete_type_to_its_own_generic_overload()
    {
        // given
        var cache = new IntegrationEventPublishInvokerCache();
        var bus = new RecordingOutboxBus();
        IIntegrationEvent first = new OrderPlaced("order");
        IIntegrationEvent second = new PaymentCaptured("payment");

        // when
        await cache.GetPublishInvoker(first.GetType())(bus, first, AbortToken);
        await cache.GetPublishInvoker(second.GetType())(bus, second, AbortToken);

        // then
        bus.Published.Select(x => x.GenericType).Should().Equal(typeof(OrderPlaced), typeof(PaymentCaptured));
    }

    #endregion

    #region Dispatcher

    [Fact]
    public async Task dispatch_async_should_be_noop_for_empty_event_list()
    {
        // given — an empty list must short-circuit without publishing anything.
        var bus = new RecordingOutboxBus();
        var dispatcher = new OutboxIntegrationEventDispatcher(
            bus,
            _AmbientCoordinator(),
            new IntegrationEventPublishInvokerCache()
        );
        // when
        await dispatcher.DispatchAsync([], AbortToken);

        // then
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task dispatch_async_should_publish_all_events_through_outbox_bus()
    {
        // given — the pipeline opened a coordinated transaction, so the outbox writer enlists on the ambient
        // coordinator. The dispatcher only fans the events out to the bus; it does not touch the transaction.
        var bus = new RecordingOutboxBus();
        var dispatcher = new OutboxIntegrationEventDispatcher(
            bus,
            _AmbientCoordinator(),
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<IIntegrationEvent> events = [new OrderPlaced("order-1"), new PaymentCaptured("payment-1")];

        // when
        await dispatcher.DispatchAsync(events, AbortToken);

        // then
        bus.Published.Should().HaveCount(2);
        bus.Published[0].GenericType.Should().Be<OrderPlaced>();
        bus.Published[1].GenericType.Should().Be<PaymentCaptured>();
    }

    [Fact]
    public async Task dispatch_async_should_propagate_publish_failure()
    {
        // given
        var bus = new ThrowingOutboxBus();
        var dispatcher = new OutboxIntegrationEventDispatcher(
            bus,
            _AmbientCoordinator(),
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<IIntegrationEvent> events = [new OrderPlaced("order-1")];

        // when
        var act = async () => await dispatcher.DispatchAsync(events, AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("Publish failed");
    }

    [Fact]
    public async Task dispatch_async_should_propagate_cancellation_before_publishing()
    {
        // given — a pre-cancelled token with a non-empty event list. The per-event loop trips
        // ThrowIfCancellationRequested on the first iteration, so nothing is published.
        var bus = new RecordingOutboxBus();
        var dispatcher = new OutboxIntegrationEventDispatcher(
            bus,
            _AmbientCoordinator(),
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<IIntegrationEvent> events = [new OrderPlaced("order-1")];
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await dispatcher.DispatchAsync(events, cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public void dispatch_sync_should_forward_to_dispatch_async_and_publish_all_events()
    {
        // given
        var bus = new RecordingOutboxBus();
        var dispatcher = new OutboxIntegrationEventDispatcher(
            bus,
            _AmbientCoordinator(),
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<IIntegrationEvent> events = [new OrderPlaced("order-1")];

        // when
        dispatcher.Dispatch(events);

        // then
        bus.Published.Should().ContainSingle();
        bus.Published[0].GenericType.Should().Be<OrderPlaced>();
    }

    [Fact]
    public async Task dispatch_async_should_fail_loud_when_no_ambient_coordinator()
    {
        // given — integration events emitted while saving inside a caller-managed transaction that was never
        // enlisted in commit coordination: no coordinator is ambient, so dispatching would be non-atomic. The
        // dispatcher must fail loud instead of shipping a message a caller rollback can no longer recall.
        var bus = new RecordingOutboxBus();
        var coordinator = Substitute.For<ICurrentCommitCoordinator>();
        coordinator.Current.Returns((ICommitCoordinator?)null);
        var dispatcher = new OutboxIntegrationEventDispatcher(
            bus,
            coordinator,
            new IntegrationEventPublishInvokerCache()
        );
        IReadOnlyList<IIntegrationEvent> events = [new OrderPlaced("order-1")];

        // when
        var act = async () => await dispatcher.DispatchAsync(events, AbortToken);

        // then — fails loud and publishes nothing
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*not enlisted in commit coordination*"
        );
        bus.Published.Should().BeEmpty();
    }

    #endregion

    #region Setup

    [Fact]
    public void add_integration_event_outbox_should_register_dispatcher_scoped_and_return_builder()
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
    public void add_integration_event_outbox_should_be_idempotent()
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

    #endregion
}
