// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// ─── Message types ────────────────────────────────────────────────────────────

public sealed record CoordinatedOrderPlaced(string Id);

public sealed class CoordinatedOrderPlacedConsumer : IConsume<CoordinatedOrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<CoordinatedOrderPlaced> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed record StandaloneOrderPlaced(string Id);

public sealed class StandaloneOrderPlacedConsumer : IConsume<StandaloneOrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<StandaloneOrderPlaced> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

// ─── Tests ────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="MessagingTestHarness.RunInUnitOfWorkAsync(Func{IServiceProvider, Task})"/> opens a service scope with
/// a resource-less unit of work active, so a test can exercise a transaction-coordinated publish: a completion
/// stores and dispatches the captured rows and a rollback discards them. Coordination is independent of the
/// consumer inbox tier, so a coordinated publish sits beside a durable consumer even on in-memory storage's
/// ProcessLocal inbox.
/// </summary>
public sealed class CoordinatedHarnessTests : TestBase
{
    private static Task<MessagingTestHarness> _CreatePublishOnlyHarnessAsync()
    {
        return MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemoryStorage();
                setup.Bus.ForMessage<CoordinatedOrderPlaced>(message => message.Contract("coordinated-order-placed"));
            });
        });
    }

    private static Task<MessagingTestHarness> _CreateConsumerHarnessAsync()
    {
        return MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemoryStorage();
                setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                setup.Bus.ForMessage<StandaloneOrderPlaced>(message =>
                    message
                        .Contract("standalone-order-placed")
                        .Consumer<StandaloneOrderPlacedConsumer>(consumer =>
                            consumer.ConsumerIdentity("tests.messaging-testing.standalone-order-placed")
                        )
                );
            });
        });
    }

    [Fact]
    public async Task should_record_coordinated_message_when_scope_commits()
    {
        await using var harness = await _CreatePublishOnlyHarnessAsync();

        await harness.RunInUnitOfWorkAsync(sp =>
            sp.GetRequiredService<IBus>().PublishAsync(new CoordinatedOrderPlaced("C1"), cancellationToken: AbortToken)
        );

        var published = await harness.WaitForPublished<CoordinatedOrderPlaced>(TimeSpan.FromSeconds(5), AbortToken);

        published.Message.Should().BeOfType<CoordinatedOrderPlaced>().Which.Id.Should().Be("C1");
        published.RequestedDeliveryMode.Should().Be(DeliveryMode.Durable);
        published.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
        published.IsCoordinated.Should().BeTrue();
        harness.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task should_record_nothing_when_scope_rolls_back()
    {
        await using var harness = await _CreatePublishOnlyHarnessAsync();

        var act = () =>
            harness.RunInUnitOfWorkAsync(async sp =>
            {
                await sp.GetRequiredService<IBus>()
                    .PublishAsync(new CoordinatedOrderPlaced("C2"), cancellationToken: AbortToken);
                throw new InvalidOperationException("boom");
            });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        // A later completed publish flows through the same single-threaded sender: had the rolled-back row been
        // handed to the dispatcher it would have been recorded before this one.
        await harness.RunInUnitOfWorkAsync(sp =>
            sp.GetRequiredService<IBus>().PublishAsync(new CoordinatedOrderPlaced("C3"), cancellationToken: AbortToken)
        );
        await harness.WaitForPublished<CoordinatedOrderPlaced>(
            m => string.Equals(m.Id, "C3", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            AbortToken
        );

        harness
            .Published.Should()
            .ContainSingle()
            .Which.Message.Should()
            .BeOfType<CoordinatedOrderPlaced>()
            .Which.Id.Should()
            .Be("C3");

        var monitoring = harness.ServiceProvider.GetRequiredService<IDataStorage>().GetMonitoringApi();
        var page = await monitoring.GetMessagesAsync(
            new MessageQuery
            {
                MessageType = MessageType.Publish,
                CurrentPage = 0,
                PageSize = 10,
            },
            AbortToken
        );
        page.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task should_return_delegate_result_after_commit()
    {
        await using var harness = await _CreatePublishOnlyHarnessAsync();

        var result = await harness.RunInUnitOfWorkAsync(async sp =>
        {
            await sp.GetRequiredService<IBus>()
                .PublishAsync(new CoordinatedOrderPlaced("C4"), cancellationToken: AbortToken);
            return 42;
        });

        result.Should().Be(42);
        await harness.WaitForPublished<CoordinatedOrderPlaced>(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_record_uncoordinated_publish_outside_scope()
    {
        await using var harness = await _CreatePublishOnlyHarnessAsync();

        // harness.Publisher carries no unit of work, so the same publish is recorded as standalone — the
        // counterpart to the coordinated recording above, and distinct from a row that recorded no answer.
        await harness.Publisher.PublishAsync(new CoordinatedOrderPlaced("C5"), cancellationToken: AbortToken);

        var published = await harness.WaitForPublished<CoordinatedOrderPlaced>(TimeSpan.FromSeconds(5), AbortToken);

        published.IsCoordinated.Should().BeFalse();
    }

    [Fact]
    public async Task should_allow_coordinated_publish_beside_consumers_on_process_local_inbox()
    {
        // Transaction coordination is independent of the consumer inbox tier: the registration boots fine next to
        // a durable consumer even though in-memory storage only offers the ProcessLocal inbox tier, and the
        // message consumes normally once published inside a unit of work.
        await using var harness = await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddHeadlessMessaging(setup =>
                {
                    setup.UseInMemory();
                    setup.UseInMemoryStorage();
                    setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                    setup.Bus.ForMessage<CoordinatedOrderPlaced>(message =>
                        message
                            .Contract("coordinated-order-placed")
                            .Consumer<CoordinatedOrderPlacedConsumer>(consumer =>
                                consumer.ConsumerIdentity("tests.messaging-testing.coordinated-order-placed")
                            )
                    );
                });
            },
            AbortToken
        );

        await harness.RunInUnitOfWorkAsync(sp =>
            sp.GetRequiredService<IBus>().PublishAsync(new CoordinatedOrderPlaced("C6"), cancellationToken: AbortToken)
        );

        var consumed = await harness.WaitForConsumed<CoordinatedOrderPlaced>(TimeSpan.FromSeconds(5), AbortToken);
        consumed.Message.Should().BeOfType<CoordinatedOrderPlaced>().Which.Id.Should().Be("C6");
    }

    [Fact]
    public async Task should_enlist_default_publish_in_scope_and_consume_after_commit()
    {
        await using var harness = await _CreateConsumerHarnessAsync();

        // A Durable publish inside an active unit of work is captured on it, so a rollback discards it.
        var act = () =>
            harness.RunInUnitOfWorkAsync(async sp =>
            {
                await sp.GetRequiredService<IBus>()
                    .PublishAsync(new StandaloneOrderPlaced("S1"), cancellationToken: AbortToken);
                throw new InvalidOperationException("boom");
            });
        await act.Should().ThrowAsync<InvalidOperationException>();

        await harness.RunInUnitOfWorkAsync(sp =>
            sp.GetRequiredService<IBus>().PublishAsync(new StandaloneOrderPlaced("S2"), cancellationToken: AbortToken)
        );
        var consumed = await harness.WaitForConsumed<StandaloneOrderPlaced>(TimeSpan.FromSeconds(5), AbortToken);

        consumed.Message.Should().BeOfType<StandaloneOrderPlaced>().Which.Id.Should().Be("S2");
        consumed.RequestedDeliveryMode.Should().Be(DeliveryMode.Durable);
        consumed.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
        harness.Published.Should().ContainSingle();
        harness.Consumed.Should().ContainSingle();
    }

    [Fact]
    public async Task should_preserve_action_exception_when_the_rollback_also_faults()
    {
        // OnFailed faults are logged, never propagated; a scope-local state whose disposal throws is the one
        // rollback fault that reaches the caller, and it must travel with the action's exception, not replace it.
        await using var harness = await _CreatePublishOnlyHarnessAsync();

        var actionEx = new InvalidOperationException("action failed");
        var rollbackEx = new InvalidOperationException("scope-state disposal failed");

        var act = () =>
            harness.RunInUnitOfWorkAsync(async services =>
            {
                var unitOfWork = services.GetRequiredService<IUnitOfWorkManager>().Current;
                unitOfWork.Should().NotBeNull();
                unitOfWork!.GetOrAdd(_ => new ThrowingDisposable(rollbackEx));

                await Task.Yield();
                throw actionEx;
            });

        var ex = await act.Should().ThrowAsync<AggregateException>();
        ex.Which.InnerExceptions.Should().Contain(actionEx);
        ex.Which.InnerExceptions.Should()
            .Contain(e => ReferenceEquals(e, rollbackEx) || e.InnerException == rollbackEx);
    }

    private sealed class ThrowingDisposable(Exception fault) : IDisposable
    {
        public void Dispose() => throw fault;
    }
}
