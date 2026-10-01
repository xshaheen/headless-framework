// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Helpers;
using Tests.Registration;

namespace Tests.Processor;

/// <summary>
/// Two <c>ConsumeOnly</c> hosts that split the consumers between them share one store. Each host's retry processor
/// must pick up only the received rows of consumers it runs: a row it has no executor for would otherwise be deferred
/// as an inbox orphan or handed to a dispatcher that fails it as having no subscriber.
/// </summary>
public sealed class RetryProcessorConsumeFilterTests : TestBase
{
    [Fact]
    public async Task should_leave_another_hosts_inbox_retry_for_that_host_to_retry()
    {
        // given
        await using var orders = _BuildHost(TestConsumers.Shipment);
        await using var billing = _BuildHost(TestConsumers.InvoiceProjection, orders.Storage);
        var storageId = await _SeedDueInboxRowAsync(
            orders.Storage,
            billing.Descriptor(TestConsumers.InvoiceProjection)
        );

        // when
        await orders.RunReceivedRetryCycleAsync();
        await billing.RunReceivedRetryCycleAsync();

        // then - the orders host neither dispatched nor orphan-deferred the billing row, so the billing host's
        // ordinary retry pickup still finds it due and dispatches it
        await orders
            .Dispatcher.DidNotReceive()
            .EnqueueToExecute(
                Arg.Any<MediumMessage>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            );
        await billing
            .Dispatcher.Received(1)
            .EnqueueToExecute(
                Arg.Is<MediumMessage>(x => x.StorageId == storageId && !x.IsInboxOrphaned),
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_leave_another_hosts_non_inbox_retry_for_that_host_to_retry()
    {
        // given
        await using var orders = _BuildHost(TestConsumers.Shipment);
        await using var billing = _BuildHost(TestConsumers.InvoiceProjection, orders.Storage);
        var storageId = await _SeedDueNonInboxRowAsync(
            orders.Storage,
            billing.Descriptor(TestConsumers.InvoiceProjection)
        );

        // when
        await orders.RunReceivedRetryCycleAsync();
        await billing.RunReceivedRetryCycleAsync();

        // then - the orders host never leased the row, which its executor would have failed as having no subscriber
        await orders
            .Dispatcher.DidNotReceive()
            .EnqueueToExecute(
                Arg.Any<MediumMessage>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            );
        await billing
            .Dispatcher.Received(1)
            .EnqueueToExecute(Arg.Is<MediumMessage>(x => x.StorageId == storageId), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_retry_every_consumers_rows_on_an_unfiltered_host()
    {
        // given
        await using var host = _BuildHost(consumeOnly: null);
        var billingRow = await _SeedDueNonInboxRowAsync(host.Storage, host.Descriptor(TestConsumers.InvoiceProjection));
        var ordersRow = await _SeedDueInboxRowAsync(host.Storage, host.Descriptor(TestConsumers.Shipment));

        // when
        await host.RunReceivedRetryCycleAsync();

        // then
        await host
            .Dispatcher.Received(1)
            .EnqueueToExecute(
                Arg.Is<MediumMessage>(x => x.StorageId == billingRow),
                null,
                Arg.Any<CancellationToken>()
            );
        await host
            .Dispatcher.Received(1)
            .EnqueueToExecute(Arg.Is<MediumMessage>(x => x.StorageId == ordersRow), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_retry_an_attached_runtime_subscriptions_failed_row_on_a_consume_only_host()
    {
        // given - ConsumeOnly never filters a runtime subscription, and only the host holding the delegate can run it
        await using var host = _BuildHost(TestConsumers.Shipment);
        var subscription = host.AttachRuntimeSubscription(_LiveInvoices);
        var storageId = await _SeedDueNonInboxRowAsync(host.Storage, subscription);

        // when
        await host.RunReceivedRetryCycleAsync();

        // then
        await host
            .Dispatcher.Received(1)
            .EnqueueToExecute(Arg.Is<MediumMessage>(x => x.StorageId == storageId), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_bind_attached_competing_runtime_subscriptions_to_both_received_pickups()
    {
        // given - a competing runtime subscription stores rows under its identity; an every-instance one stores none
        var storage = Substitute.For<IDataStorage>();
        storage
            .GetReceivedMessagesOfNeedRetryAsync(
                Arg.Any<MessageLane>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        storage
            .GetReceivedInboxOrphansOfNeedRetryAsync(
                Arg.Any<MessageLane>(),
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
        var runtime = Substitute.For<IRuntimeConsumerRegistry>();
        runtime
            .GetDescriptors()
            .Returns([
                _RuntimeDescriptor(_LiveInvoices, everyInstance: false),
                _RuntimeDescriptor("billing-live.prices", everyInstance: true),
            ]);
        await using var host = _BuildHost(TestConsumers.Shipment, storage, runtime);

        // when
        await host.RunReceivedRetryCycleAsync();

        // then
        string[] expected = [_LiveInvoices, TestConsumers.Shipment];
        await storage
            .Received(1)
            .GetReceivedMessagesOfNeedRetryAsync(
                MessageLane.Bus,
                Arg.Is<IReadOnlyCollection<string>?>(x => x != null && x.SequenceEqual(expected)),
                Arg.Any<CancellationToken>()
            );
        await storage
            .Received(1)
            .GetReceivedInboxOrphansOfNeedRetryAsync(
                MessageLane.Bus,
                Arg.Is<IReadOnlyCollection<string>?>(x => x != null && x.SequenceEqual(expected)),
                Arg.Any<CancellationToken>()
            );
    }

    // A competing runtime subscription to a message no ConsumeOnly-selected consumer handles.
    private const string _LiveInvoices = "billing-live.invoices";

    private static Host _BuildHost(
        string? consumeOnly,
        IDataStorage? sharedStorage = null,
        IRuntimeConsumerRegistry? runtimeRegistry = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton<HostControlProbe>();
        services.ConfigureMessaging(messaging =>
        {
            messaging.AddModule<BillingModule>().AddModule<OrdersModule>();
            messaging.Message<InvoiceIssued>("billing.invoice-issued");
            messaging.Message<OrderShipped>("orders.order-shipped");
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseProcessLocalInMemoryStorage();
            if (consumeOnly is not null)
            {
                setup.ConsumeOnly(consumeOnly);
            }
        });
        if (sharedStorage is not null)
        {
            services.AddSingleton(sharedStorage);
        }

        if (runtimeRegistry is not null)
        {
            services.AddSingleton(runtimeRegistry);
        }

        var provider = services.BuildServiceProvider();
        var dispatcher = Substitute.For<IDispatcher>();
        var processor = new MessageNeedToRetryProcessor(
            Options.Create(new MessagingOptions()),
            Options.Create(new RetryProcessorOptions { BaseInterval = TimeSpan.Zero, AdaptivePolling = false }),
            NullLogger<MessageNeedToRetryProcessor>.Instance,
            dispatcher,
            Substitute.For<IDistributedLock>(),
            consumerResolver: provider.GetRequiredService<MethodMatcherCache>(),
            hostOwnership: provider.GetRequiredService<IConsumerHostOwnership>()
        );

        return new Host(provider, processor, dispatcher);
    }

    // Leaves an admitted inbox row unleased and due now, as a failed attempt awaiting its retry would be.
    private static async Task<Guid> _SeedDueInboxRowAsync(IDataStorage storage, ConsumerExecutorDescriptor descriptor)
    {
        var message = (
            await storage.AdmitReceivedMessageAsync(
                descriptor.MessageName,
                descriptor.ResolvedConsumerIdentity,
                descriptor.MessageContractVersion,
                _Envelope(descriptor),
                cancellationToken: AbortToken
            )
        ).Message;
        var attempts = message.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(message, TimeSpan.FromMinutes(5), attempts, AbortToken))
            .Should()
            .BeTrue();
        var identity = new MessageLeaseIdentity(
            message.StorageId,
            message.Owner,
            message.LockedUntil!.Value,
            MessageLane.Bus,
            message.InboxAttemptFence
        );
        (
            await ((ICircuitRetryDeferralStorage)storage).DeferReceivedRetryAsync(
                new CircuitRetryDeferral(identity, DateTimeOffset.UtcNow.AddMinutes(-5)),
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        return message.StorageId;
    }

    private static async Task<Guid> _SeedDueNonInboxRowAsync(
        IDataStorage storage,
        ConsumerExecutorDescriptor descriptor
    )
    {
        var stored = await storage.StoreReceivedMessageAsync(
            descriptor.MessageName,
            descriptor.ResolvedConsumerIdentity,
            _Envelope(descriptor),
            AbortToken
        );
        (
            await storage.ChangeReceiveStateAsync(
                stored,
                StatusName.Failed,
                nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        return stored.StorageId;
    }

    private static ConsumerExecutorDescriptor _RuntimeDescriptor(string identity, bool everyInstance) =>
        new()
        {
            ConsumerType = typeof(RetryProcessorConsumeFilterTests),
            MessageName = "billing.invoice-issued",
            SubscriptionName = identity,
            Lane = MessageLane.Bus,
            EveryInstance = everyInstance,
        };

    private static MediumMessage _Envelope(ConsumerExecutorDescriptor descriptor) =>
        new()
        {
            StorageId = Guid.Empty,
            Content = string.Empty,
            Lane = MessageLane.Bus,
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = Guid.NewGuid().ToString(),
                    [Headers.MessageName] = descriptor.MessageName,
                    [Headers.ConsumerIdentity] = descriptor.ResolvedConsumerIdentity,
                },
                "payload"
            ),
        };

    private sealed class Host(ServiceProvider provider, MessageNeedToRetryProcessor processor, IDispatcher dispatcher)
        : IAsyncDisposable
    {
        public IDataStorage Storage { get; } = provider.GetRequiredService<IDataStorage>();

        public IDispatcher Dispatcher { get; } = dispatcher;

        // The selector lists only the consumers this host runs, so a seeded row always matches a real executor.
        public ConsumerExecutorDescriptor Descriptor(string identity) =>
            provider
                .GetRequiredService<IConsumerServiceSelector>()
                .SelectCandidates()
                .SingleOrDefault(x => string.Equals(x.ConsumerIdentity, identity, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Host does not run consumer '{identity}'.");

        public ConsumerExecutorDescriptor AttachRuntimeSubscription(string identity)
        {
            var runtime = provider.GetRequiredService<IRuntimeConsumerRegistry>();
            runtime.Register<InvoiceIssued>(
                static (_, _, _) => ValueTask.CompletedTask,
                new RuntimeSubscriptionOptions { Identity = identity, HandlerId = identity }
            );
            // The subscriber invalidates the executor cache on attach; registering directly must do the same.
            provider.GetRequiredService<MethodMatcherCache>().Invalidate();

            return runtime
                .GetDescriptors()
                .Single(x => string.Equals(x.ResolvedConsumerIdentity, identity, StringComparison.Ordinal));
        }

        public async Task RunReceivedRetryCycleAsync()
        {
            await using var context = new ProcessingContext(provider, TimeProvider.System, AbortToken);
            processor.MarkQuadrantDueForTest(MessageType.Subscribe, MessageLane.Bus);
            await processor.ProcessAsync(context);
            await processor.WaitForQuadrantIdleForTestAsync(MessageType.Subscribe, MessageLane.Bus);
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
