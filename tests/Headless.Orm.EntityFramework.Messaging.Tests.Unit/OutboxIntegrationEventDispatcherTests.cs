// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Messaging;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Tests;

public sealed class OutboxIntegrationEventDispatcherTests
{
    #region Test Infrastructure

    private sealed record OrderPlaced(string UniqueId) : IIntegrationEvent;

    private sealed record PaymentCaptured(string UniqueId) : IIntegrationEvent;

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
        await invoke(bus, integrationEvent, TestContext.Current.CancellationToken);

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
        await cache.GetPublishInvoker(first.GetType())(bus, first, TestContext.Current.CancellationToken);
        await cache.GetPublishInvoker(second.GetType())(bus, second, TestContext.Current.CancellationToken);

        // then
        bus.Published.Select(x => x.GenericType).Should().Equal(typeof(OrderPlaced), typeof(PaymentCaptured));
    }

    #endregion

    #region Dispatcher

    [Fact]
    public async Task dispatch_async_should_be_noop_for_empty_event_list()
    {
        // given — an empty list must short-circuit before resolving the outbox transaction, so a provider
        // with no IAmbientTransaction registered must not throw.
        var bus = new RecordingOutboxBus();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new OutboxIntegrationEventDispatcher(services, bus, new IntegrationEventPublishInvokerCache());
        var transaction = Substitute.For<IDbContextTransaction>();

        // when
        await dispatcher.DispatchAsync([], transaction, TestContext.Current.CancellationToken);

        // then
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task dispatch_async_should_resolve_outbox_transaction_publish_all_events_and_cleanup()
    {
        // given
        var bus = new RecordingOutboxBus();
        var outboxTransaction = Substitute.For<IAmbientTransaction>();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(outboxTransaction);
        await using var services = serviceCollection.BuildServiceProvider();
        var dispatcher = new OutboxIntegrationEventDispatcher(services, bus, new IntegrationEventPublishInvokerCache());
        var transaction = Substitute.For<IDbContextTransaction>();
        IReadOnlyList<IIntegrationEvent> events = [new OrderPlaced("order-1"), new PaymentCaptured("payment-1")];

        // when
        await dispatcher.DispatchAsync(events, transaction, TestContext.Current.CancellationToken);

        // then
        outboxTransaction.Received(1).DbTransaction = transaction;
        outboxTransaction.Received(1).AutoCommit = false;
        outboxTransaction.Received(1).DbTransaction = null;

        bus.Published.Should().HaveCount(2);
        bus.Published[0].GenericType.Should().Be<OrderPlaced>();
        bus.Published[1].GenericType.Should().Be<PaymentCaptured>();
    }

    [Fact]
    public async Task dispatch_async_should_cleanup_transaction_when_publish_fails()
    {
        // given
        var bus = new ThrowingOutboxBus();
        var outboxTransaction = Substitute.For<IAmbientTransaction>();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(outboxTransaction);
        await using var services = serviceCollection.BuildServiceProvider();
        var dispatcher = new OutboxIntegrationEventDispatcher(services, bus, new IntegrationEventPublishInvokerCache());
        var transaction = Substitute.For<IDbContextTransaction>();
        IReadOnlyList<IIntegrationEvent> events = [new OrderPlaced("order-1")];

        // when — await directly so the task completes before the provider is disposed (CA2025).
        InvalidOperationException? caught = null;
        try
        {
            await dispatcher.DispatchAsync(events, transaction, TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            caught = exception;
        }

        // then
        caught.Should().NotBeNull();
        caught.Message.Should().Be("Publish failed");
        outboxTransaction.Received(1).DbTransaction = transaction;
        outboxTransaction.Received(1).DbTransaction = null;
    }

    [Fact]
    public async Task dispatch_async_should_cleanup_transaction_when_cancelled_before_publishing()
    {
        // given — a pre-cancelled token with a non-empty event list. The per-event loop trips
        // ThrowIfCancellationRequested on the first iteration, so the OperationCanceledException must
        // propagate while the finally-block still detaches the outbox transaction (DbTransaction = null).
        var bus = new RecordingOutboxBus();
        var outboxTransaction = Substitute.For<IAmbientTransaction>();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(outboxTransaction);
        await using var services = serviceCollection.BuildServiceProvider();
        var dispatcher = new OutboxIntegrationEventDispatcher(services, bus, new IntegrationEventPublishInvokerCache());
        var transaction = Substitute.For<IDbContextTransaction>();
        IReadOnlyList<IIntegrationEvent> events = [new OrderPlaced("order-1")];
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when — await directly so the task completes before the provider is disposed (CA2025).
        var act = async () => await dispatcher.DispatchAsync(events, transaction, cts.Token);

        // then — cancellation propagates and the finally-block cleanup still ran.
        await act.Should().ThrowAsync<OperationCanceledException>();
        outboxTransaction.Received(1).DbTransaction = null;
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public void dispatch_sync_should_forward_to_dispatch_async_and_publish_all_events()
    {
        // given
        var bus = new RecordingOutboxBus();
        var outboxTransaction = Substitute.For<IAmbientTransaction>();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(outboxTransaction);
        using var services = serviceCollection.BuildServiceProvider();
        var dispatcher = new OutboxIntegrationEventDispatcher(services, bus, new IntegrationEventPublishInvokerCache());
        var transaction = Substitute.For<IDbContextTransaction>();
        IReadOnlyList<IIntegrationEvent> events = [new OrderPlaced("order-1")];

        // when
        dispatcher.Dispatch(events, transaction);

        // then
        outboxTransaction.Received(1).DbTransaction = transaction;
        outboxTransaction.Received(1).DbTransaction = null;
        bus.Published.Should().ContainSingle();
        bus.Published[0].GenericType.Should().Be<OrderPlaced>();
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
