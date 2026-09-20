// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// Top-level and public on purpose: the harness's transport recorder resolves a published payload back to its
// contract type by name, which a private nested type does not expose.
public sealed record OutboxOrderPlaced(string UniqueId);

/// <summary>
/// The dispatcher's whole promise: integration events raised during a save become durable outbox rows only if
/// that save commits. Proven against a live in-memory messaging host rather than a substituted publisher,
/// because a substitute cannot express whether a publish enlisted — which is exactly what breaks when the
/// dispatcher publishes through the autonomous <see cref="IBus"/> instead of the save's unit of work.
/// </summary>
public sealed class OutboxIntegrationEventDispatcherAtomicityTests : TestBase
{
    /// <summary>
    /// A unit-of-work resource that is not relational. The dispatcher refuses to run without a resource, while
    /// the in-memory storage refuses only a relational one (it cannot be atomic with a database transaction),
    /// so this is the one resource shape that satisfies both and keeps the proof off Docker.
    /// </summary>
    private sealed class NonRelationalResource : IUnitOfWorkResource
    {
        public bool IsOwned => true;

        public bool IsTransactionCompleted { get; private set; }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            IsTransactionCompleted = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            IsTransactionCompleted = true;
            return ValueTask.CompletedTask;
        }
    }

    private static Task<MessagingTestHarness> _CreateHarnessAsync()
    {
        return MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemoryStorage();
                setup.Bus.ForMessage<OutboxOrderPlaced>(message => message.Contract("order-placed"));
            });

            services.AddHeadlessDbContextServices().AddIntegrationEventOutbox();
        });
    }

    private static async Task<int> _DurableRowCountAsync(MessagingTestHarness harness)
    {
        var page = await harness
            .ServiceProvider.GetRequiredService<IDataStorage>()
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    CurrentPage = 0,
                    PageSize = 20,
                },
                AbortToken
            );

        return page.Items.Count;
    }

    [Fact]
    public async Task should_leave_no_durable_row_when_the_save_rolls_back()
    {
        // given — the save pipeline's transaction is the scope's current unit of work
        await using var harness = await _CreateHarnessAsync();
        await using var scope = harness.ServiceProvider.CreateAsyncScope();
        var unitOfWork = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWorkManager>()
            .BeginAsync(
                _ => ValueTask.FromResult<IUnitOfWorkResource>(new NonRelationalResource()),
                options: null,
                AbortToken
            );

        // when — entities raised an integration event during the save, and the save then rolls back
        await using (unitOfWork)
        {
            await scope
                .ServiceProvider.GetRequiredService<IHeadlessOutboxDispatcher>()
                .DispatchAsync([EventContext.Capture<object>(new OutboxOrderPlaced("rolled-back"))], AbortToken);

            // Still nothing durable: the row waits on the unit until it commits.
            (await _DurableRowCountAsync(harness))
                .Should()
                .Be(0);

            await unitOfWork.RollbackAsync();
        }

        // then — the rollback discarded the event; an autonomous publish would have left the row behind
        (await _DurableRowCountAsync(harness))
            .Should()
            .Be(0);
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task should_write_one_durable_row_per_event_and_dispatch_after_the_commit()
    {
        // given
        await using var harness = await _CreateHarnessAsync();
        await using var scope = harness.ServiceProvider.CreateAsyncScope();
        var unitOfWork = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWorkManager>()
            .BeginAsync(
                _ => ValueTask.FromResult<IUnitOfWorkResource>(new NonRelationalResource()),
                options: null,
                AbortToken
            );

        // when
        await using (unitOfWork)
        {
            await scope
                .ServiceProvider.GetRequiredService<IHeadlessOutboxDispatcher>()
                .DispatchAsync(
                    [
                        EventContext.Capture<object>(new OutboxOrderPlaced("committed-1")),
                        EventContext.Capture<object>(new OutboxOrderPlaced("committed-2")),
                    ],
                    AbortToken
                );

            // Nothing is dispatched before the commit — the rows are held on the unit.
            (await _DurableRowCountAsync(harness))
                .Should()
                .Be(0);
            harness.Published.Should().BeEmpty();

            await unitOfWork.CompleteAsync(AbortToken);
        }

        // then — one durable row per event, dispatched to the broker after the commit
        await harness.WaitForPublished<OutboxOrderPlaced>(
            message => string.Equals(message.UniqueId, "committed-2", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            AbortToken
        );
        (await _DurableRowCountAsync(harness)).Should().Be(2);
        harness
            .Published.Select(record => ((OutboxOrderPlaced)record.Message).UniqueId)
            .Should()
            .BeEquivalentTo("committed-1", "committed-2");
    }
}
