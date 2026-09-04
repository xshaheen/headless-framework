// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

/// <summary>
/// Proves the central commit-coordination invariant: <b>the commit signal is acceleration, not
/// correctness</b>. The durable outbox row written inside the business transaction plus the relay sweep
/// (<c>GetPublishedMessagesOfNeedRetryAsync</c>) are the source of truth; the in-memory drain only makes
/// dispatch faster. Here the commit signal is deliberately dropped — the scope is disposed un-signalled,
/// exactly what happens when a diagnostic is missed, an interceptor is not wired, or an inline provider's
/// caller forgets to signal — and the test asserts no message is lost: the accelerator never fires, yet the
/// relay pickup claims the durable row for dispatch.
/// </summary>
public sealed class DropSignalRelayRecoveryTests : TestBase
{
    [Fact]
    public async Task missed_commit_signal_must_not_lose_work_because_relay_pickup_recovers_the_durable_row()
    {
        // Real in-memory storage from the production registration path. A fake clock lets the test
        // jump past InitialDispatchGrace so the stored row becomes due for the relay sweep without waiting.
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IDataStorage>();

        await using var transaction = new TestDbTransaction();
        var stack = new CommitScopeStack();
        var dispatcher = Substitute.For<IDispatcher>();

        var writer = new OutboxMessageWriter(storage, dispatcher, TimeProvider.System);

        var scope = new CommitScopeFactory(stack).Begin(
            new EmptyServiceProvider(),
            [new RelationalCommitContext(() => null, () => transaction)]
        );

        await using (scope)
        {
            // Stores the durable row in-transaction and buffers the accelerator dispatch on the coordinator.
            var request = _CreatePublishRequestFactory().Create(new RelayMessage("value"), lane: MessageLane.Bus);
            var decision = DeliveryDecisionResolver.Resolve(
                MessageLane.Bus,
                DeliveryMode.Durable,
                delay: null,
                DeliveryCoordination.Compatible(stack.Current!, transaction),
                TimeProvider.System.GetUtcNow()
            );
            await writer.WriteAsync(request, decision, AbortToken);
        }

        // The signal was DROPPED (un-signalled dispose drains as rollback): the accelerator must not fire.
        await dispatcher.DidNotReceive().EnqueueToPublish(Arg.Any<MediumMessage>(), Arg.Any<CancellationToken>());
        await dispatcher
            .DidNotReceive()
            .EnqueueToScheduler(
                Arg.Any<MediumMessage>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<CancellationToken>()
            );

        // Correctness floor: the durable row (committed by the database in production — the in-memory
        // storage models the committed state) is claimed by the relay sweep and will be dispatched.
        time.Advance(TimeSpan.FromMinutes(5)); // jump past InitialDispatchGrace so the row is due
        var recovered = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)).ToList();

        recovered
            .Should()
            .ContainSingle("a missed commit signal must degrade latency only — the relay sweep owns recovery")
            .Which.Origin.Headers[Headers.MessageName]
            .Should()
            .Be("relay.message");
    }

    private static MessagePublishRequestFactory _CreatePublishRequestFactory()
    {
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(RelayMessage), "relay.message");

        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            Options.Create(new MessagingOptions()),
            registry,
            new NullCurrentTenant()
        );
    }

    private sealed record RelayMessage(string Value);

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class NoopPublishMiddlewarePipeline : IPublishMiddlewarePipeline
    {
        public Task ExecuteAsync(
            object? contentObj,
            Type declaredMessageType,
            MessageLane lane,
            MessageOptions? messageOptions,
            DeliveryDecision decision,
            Func<MessageOptions?, CancellationToken, Task> innerPublish,
            CancellationToken cancellationToken = default
        )
        {
            return innerPublish(messageOptions, cancellationToken);
        }

        public Task ExecuteAsync<T>(
            T? contentObj,
            MessageLane lane,
            MessageOptions? messageOptions,
            DeliveryDecision decision,
            Func<MessageOptions?, CancellationToken, Task> innerPublish,
            CancellationToken cancellationToken = default
        )
        {
            return innerPublish(messageOptions, cancellationToken);
        }
    }

    private sealed class TestDbTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection? DbConnection => null;

        public override void Commit() { }

        public override void Rollback() { }
    }
}
