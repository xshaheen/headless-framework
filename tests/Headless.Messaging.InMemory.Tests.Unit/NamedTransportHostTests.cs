// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// One host with two transports, each an in-memory broker of its own, stands in for a host bridging two brokers: a
/// publish goes through the transport it names, and a consumer receives only from the transport it is bound to.
/// </summary>
public sealed class NamedTransportHostTests : TestBase
{
    private const string _Partner = "partner";
    private const string _Sentinel = "sentinel";

    [Fact]
    public async Task should_deliver_each_publish_only_to_the_consumers_bound_to_its_transport()
    {
        // given
        await using var host = await _StartHostAsync();
        var probe = host.GetRequiredService<TransportProbe>();
        var defaultBus = host.GetRequiredService<IBus>();
        var partnerBus = host.GetRequiredKeyedService<IBus>(_Partner);
        var sentinels = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Recorded += (_, _) =>
        {
            if (
                probe.Internal.Contains(_Sentinel, StringComparer.Ordinal)
                && probe.Partner.Contains(_Sentinel, StringComparer.Ordinal)
            )
            {
                sentinels.TrySetResult();
            }
        };

        // when
        await defaultBus.PublishAsync(new OrderShipped("internal-order"), cancellationToken: AbortToken);
        await partnerBus.PublishAsync(new OrderShipped("partner-order"), cancellationToken: AbortToken);

        // A sentinel published on each transport after the others reaches each consumer behind any message that is
        // still in flight, so waiting for both gives a message on the wrong broker the time to show up.
        await defaultBus.PublishAsync(new OrderShipped(_Sentinel), cancellationToken: AbortToken);
        await partnerBus.PublishAsync(new OrderShipped(_Sentinel), cancellationToken: AbortToken);
        await sentinels.Task.Bounded();

        // then
        probe.Internal.Should().BeEquivalentTo(["internal-order", _Sentinel]);
        probe.Partner.Should().BeEquivalentTo(["partner-order", _Sentinel]);
    }

    [Fact]
    public async Task should_store_the_named_transport_on_a_durable_row_published_through_it()
    {
        // given
        await using var host = await _StartHostAsync();
        var partnerBus = host.GetRequiredKeyedService<IBus>(_Partner);
        var defaultBus = host.GetRequiredService<IBus>();

        // when
        var partnerReceipt = await partnerBus.PublishAsync(
            new OrderShipped("partner-order"),
            cancellationToken: AbortToken
        );
        var defaultReceipt = await defaultBus.PublishAsync(
            new OrderShipped("internal-order"),
            cancellationToken: AbortToken
        );

        // then
        var monitoring = host.GetRequiredService<IDataStorage>().GetMonitoringApi();
        var partnerRow = await monitoring.GetPublishedMessageAsync(partnerReceipt.StorageId!.Value, AbortToken);
        var defaultRow = await monitoring.GetPublishedMessageAsync(defaultReceipt.StorageId!.Value, AbortToken);
        partnerRow!.TransportName.Should().Be(_Partner);
        defaultRow!.TransportName.Should().BeNull();
    }

    [Fact]
    public async Task should_fail_startup_when_a_consumer_is_bound_to_a_transport_the_host_does_not_register()
    {
        // given
        var services = _CreateServices(registerPartner: false);
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IConsumerRegistry>();

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'orders.partner-sync' is bound to transport 'partner'*does not register*");
    }

    [Fact]
    public async Task should_relay_a_unit_of_work_publish_through_the_transport_its_options_name()
    {
        // given
        await using var host = await _StartHostAsync();
        var probe = host.GetRequiredService<TransportProbe>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Recorded += (_, _) =>
        {
            if (probe.Partner.Contains("enlisted-order", StringComparer.Ordinal))
            {
                delivered.TrySetResult();
            }
        };
        await using var scope = host.CreateAsyncScope();
        await using var unitOfWork = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);

        // when
        var receipt = await unitOfWork.Outbox.PublishAsync(
            new OrderShipped("enlisted-order"),
            new OutboxOptions { TransportName = _Partner },
            AbortToken
        );
        await unitOfWork.CompleteAsync(AbortToken);
        await delivered.Task.Bounded();

        // then
        var row = await host.GetRequiredService<IDataStorage>()
            .GetMonitoringApi()
            .GetPublishedMessageAsync(receipt.StorageId!.Value, AbortToken);
        row!.TransportName.Should().Be(_Partner);
        probe.Internal.Should().NotContain("enlisted-order");
    }

    [Fact]
    public async Task should_refuse_a_unit_of_work_publish_naming_a_transport_the_host_does_not_register()
    {
        // given
        await using var host = await _StartHostAsync();
        await using var scope = host.CreateAsyncScope();
        await using var unitOfWork = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWorkFactory>()
            .BeginAsync(cancellationToken: AbortToken);

        // when
        var act = () =>
            unitOfWork.Outbox.PublishAsync(
                new OrderShipped("lost-order"),
                new OutboxOptions { TransportName = "billing" },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*No transport named 'billing'*");
    }

    [Fact]
    public async Task should_fail_a_stored_row_naming_a_transport_the_host_does_not_register()
    {
        // given: a row another host published through a transport this host does not register
        await using var host = await _StartHostAsync();
        var storage = host.GetRequiredService<IDataStorage>();
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "ghost-order",
                [Headers.MessageName] = nameof(OrderShipped),
            },
            new OrderShipped("ghost-order")
        );
        var stored = await storage.StoreMessageAsync(
            nameof(OrderShipped),
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = origin,
                Content = string.Empty,
                Lane = MessageLane.Bus,
                TransportName = "billing",
            },
            cancellationToken: AbortToken
        );

        // when
        var result = await host.GetRequiredService<IMessageSender>().SendAsync(stored);

        // then: the row ends instead of reaching the default broker or any other one
        result.Succeeded.Should().BeFalse();
        var row = await storage.GetMonitoringApi().GetPublishedMessageAsync(stored.StorageId, AbortToken);
        row!.StatusName.Should().Be(StatusName.Failed);
        row.ExceptionInfo.Should().Contain("transport 'billing'");
        host.GetRequiredService<TransportProbe>().Internal.Should().BeEmpty();
    }

    private static async Task<ServiceProvider> _StartHostAsync()
    {
        var provider = _CreateServices(registerPartner: true).BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private static ServiceCollection _CreateServices(bool registerPartner)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TransportProbe>();
        services.ConfigureMessaging(messaging => messaging.AddModule<OrdersModule>());
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;

            if (registerPartner)
            {
                setup.AddTransport(_Partner).UseInMemory();
            }

            setup.Tune("orders.partner-sync", consumer => consumer.Transport(_Partner));
        });

        return services;
    }
}

public sealed record OrderShipped(string OrderId);

public sealed class TransportProbe
{
    public ConcurrentQueue<string> Internal { get; } = new();

    public ConcurrentQueue<string> Partner { get; } = new();

    /// <summary>Raised after a consumer records a message, so a test waits on deliveries instead of polling.</summary>
    public event EventHandler? Recorded;

    public void Record(ConcurrentQueue<string> target, string orderId)
    {
        target.Enqueue(orderId);
        Recorded?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class InternalShipmentProjection(TransportProbe probe) : IConsume<OrderShipped>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderShipped> context, CancellationToken cancellationToken)
    {
        probe.Record(probe.Internal, context.Message.OrderId);
        return ValueTask.CompletedTask;
    }
}

public sealed class PartnerShipmentSync(TransportProbe probe) : IConsume<OrderShipped>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderShipped> context, CancellationToken cancellationToken)
    {
        probe.Record(probe.Partner, context.Message.OrderId);
        return ValueTask.CompletedTask;
    }
}

public sealed class OrdersModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<InternalShipmentProjection, OrderShipped>(
            "orders.internal-projection",
            everyInstance: false,
            _Dispatch<InternalShipmentProjection>()
        );
        catalog.AddBusConsumer<PartnerShipmentSync, OrderShipped>(
            "orders.partner-sync",
            everyInstance: false,
            _Dispatch<PartnerShipmentSync>()
        );
    }

    private static MessageConsumerDispatch _Dispatch<TConsumer>()
        where TConsumer : class, IConsume<OrderShipped> =>
        static (services, context, cancellationToken) =>
            ActivatorUtilities
                .CreateInstance<TConsumer>(services)
                .ConsumeAsync((ConsumeContext<OrderShipped>)context, cancellationToken);
}
