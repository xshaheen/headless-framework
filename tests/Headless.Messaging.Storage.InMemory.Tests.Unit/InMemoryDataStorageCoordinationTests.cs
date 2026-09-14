// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.InMemory;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// In-memory storage takes part in a non-relational coordinated scope: rows written through the coordinated
/// publish path stay invisible until the scope commits and are discarded when it rolls back. A coordinator that
/// carries a relational handle stays incompatible, because in-memory rows cannot be atomic with a database.
/// </summary>
public sealed class InMemoryDataStorageCoordinationTests : TestBase
{
    [Fact]
    public void should_report_non_relational_coordinator_compatible_without_transaction()
    {
        var storage = _CreateStorage();
        using var scope = _BeginScope();

        var coordination = _Resolver(storage).Resolve(scope.Coordinator);

        coordination.Status.Should().Be(DeliveryCoordinationStatus.Compatible);
        coordination.Coordinator.Should().BeSameAs(scope.Coordinator);
        coordination.Transaction.Should().BeNull();
    }

    [Fact]
    public void should_report_relational_coordinator_incompatible()
    {
        var storage = _CreateStorage();
        using var scope = _BeginScope(Substitute.For<IRelationalCommitContext>());

        var coordination = _Resolver(storage).Resolve(scope.Coordinator);

        coordination.Status.Should().Be(DeliveryCoordinationStatus.Incompatible);
        coordination.Mismatch.Should().Be(DeliveryCoordinationMismatch.StorageProvider);
    }

    [Fact]
    public async Task should_report_finished_coordinator_incompatible()
    {
        var storage = _CreateStorage();
        var scope = _BeginScope();
        await scope.SignalAsync(CommitOutcome.RolledBack);

        var coordination = _Resolver(storage).Resolve(scope.Coordinator);

        coordination.Status.Should().Be(DeliveryCoordinationStatus.Incompatible);
        coordination.Mismatch.Should().Be(DeliveryCoordinationMismatch.InactiveTransaction);
    }

    [Fact]
    public async Task should_hide_coordinated_row_until_commit_then_expose_it()
    {
        var storage = _CreateStorage();
        var monitoring = new InMemoryMonitoringApi(storage, TimeProvider.System);
        await using var dispatcher = new RecordingCommittedDispatcher(storage);

        Guid storageId;
        await using (var scope = _BeginScope())
        {
            storageId = await _PublishCoordinatedAsync(storage, dispatcher, scope);

            (await monitoring.GetPublishedMessageAsync(storageId, AbortToken)).Should().BeNull();
            storage.PublishedMessages.Should().BeEmpty();
            dispatcher.CommittedMessages.Should().BeEmpty();

            await scope.SignalAsync(CommitOutcome.Committed);
        }

        var published = await monitoring.GetPublishedMessageAsync(storageId, AbortToken);
        published.Should().NotBeNull();
        published.StorageId.Should().Be(storageId);
        storage.PublishedMessages.Should().ContainSingle().Which.Value.StatusName.Should().Be(StatusName.Scheduled);
        dispatcher.CommittedMessages.Should().ContainSingle().Which.StorageId.Should().Be(storageId);
    }

    [Fact]
    public async Task should_discard_coordinated_row_on_rollback()
    {
        var storage = _CreateStorage();
        await using var dispatcher = new RecordingCommittedDispatcher(storage);

        await using (var scope = _BeginScope())
        {
            await _PublishCoordinatedAsync(storage, dispatcher, scope);
            await scope.SignalAsync(CommitOutcome.RolledBack);
        }

        storage.PublishedMessages.Should().BeEmpty();
        dispatcher.CommittedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_leak_rows_between_sequential_scopes()
    {
        var storage = _CreateStorage();
        await using var dispatcher = new RecordingCommittedDispatcher(storage);

        await using (var rolledBack = _BeginScope())
        {
            await _PublishCoordinatedAsync(storage, dispatcher, rolledBack);
            await rolledBack.SignalAsync(CommitOutcome.RolledBack);
        }

        Guid committedId;
        await using (var committed = _BeginScope())
        {
            committedId = await _PublishCoordinatedAsync(storage, dispatcher, committed);
            await committed.SignalAsync(CommitOutcome.Committed);
        }

        storage.PublishedMessages.Keys.Should().Equal(committedId);
        dispatcher.CommittedMessages.Should().ContainSingle().Which.StorageId.Should().Be(committedId);
    }

    [Fact]
    public async Task should_expose_committed_row_before_dispatcher_hand_off()
    {
        // The storage buffer registers its commit callback when the row is captured, before the writer obtains
        // the outbox buffer; callbacks drain in registration order, so the dispatcher must already see the row.
        var storage = _CreateStorage();
        await using var dispatcher = new RecordingCommittedDispatcher(storage);

        await using (var scope = _BeginScope())
        {
            await _PublishCoordinatedAsync(storage, dispatcher, scope);
            await _PublishCoordinatedAsync(storage, dispatcher, scope);
            await scope.SignalAsync(CommitOutcome.Committed);
        }

        dispatcher.CommittedMessages.Should().HaveCount(2);
        dispatcher.VisibleAtHandOff.Should().Equal(true, true);
    }

    [Fact]
    public async Task should_capture_delayed_coordinated_row_and_signal_scheduler_after_commit()
    {
        var storage = _CreateStorage();
        await using var dispatcher = new RecordingCommittedDispatcher(storage);

        Guid storageId;
        await using (var scope = _BeginScope())
        {
            storageId = await _PublishCoordinatedAsync(storage, dispatcher, scope, delay: TimeSpan.FromMinutes(30));

            storage.PublishedMessages.Should().BeEmpty();
            dispatcher.CommittedDelayedMessages.Should().BeEmpty();

            await scope.SignalAsync(CommitOutcome.Committed);
        }

        var row = storage.PublishedMessages.Should().ContainKey(storageId).WhoseValue;
        row.StatusName.Should().Be(StatusName.Delayed);
        row.ExpiresAt.Should().NotBeNull();
        dispatcher.CommittedDelayedMessages.Should().ContainSingle().Which.StorageId.Should().Be(storageId);
        dispatcher.VisibleAtHandOff.Should().Equal(true);
    }

    private static async Task<Guid> _PublishCoordinatedAsync(
        InMemoryDataStorage storage,
        IDispatcher dispatcher,
        ICommitScope scope,
        TimeSpan? delay = null
    )
    {
        var writer = new OutboxMessageWriter(storage, dispatcher, TimeProvider.System);
        var request = _CreatePublishRequestFactory().Create(new CoordinatedMessage("value"), lane: MessageLane.Bus);
        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Coordinated,
            delay,
            _Resolver(storage).Resolve(scope.Coordinator),
            TimeProvider.System.GetUtcNow()
        );

        return await writer.WriteAsync(request, decision, AbortToken);
    }

    private static IDeliveryCoordinationResolver _Resolver(InMemoryDataStorage storage) => storage;

    private static ICommitScope _BeginScope(params ICommitCapability[] capabilities)
    {
        var services = new ServiceCollection().AddCommitCoordination().BuildServiceProvider();

        return services.GetRequiredService<ICommitScopeFactory>().Begin(services, capabilities);
    }

    private static InMemoryDataStorage _CreateStorage()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        var provider = services.BuildServiceProvider();

        return new InMemoryDataStorage(
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            provider.GetRequiredService<ISerializer>(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            new NullNodeMembership()
        );
    }

    private static MessagePublishRequestFactory _CreatePublishRequestFactory()
    {
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(CoordinatedMessage), "coordinated.message");

        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            Options.Create(new MessagingOptions()),
            registry,
            new NullCurrentTenant()
        );
    }

    private sealed record CoordinatedMessage(string Value);

    private sealed class RecordingCommittedDispatcher(InMemoryDataStorage storage)
        : IDispatcher,
            ICommittedMessageDispatcher,
            ICommittedDelayedMessageDispatcher
    {
        public List<MediumMessage> CommittedMessages { get; } = [];

        public List<MediumMessage> CommittedDelayedMessages { get; } = [];

        /// <summary>Whether the row was already in the published set when the dispatcher received it.</summary>
        public List<bool> VisibleAtHandOff { get; } = [];

        public void EnqueueCommittedMessage(MediumMessage message)
        {
            VisibleAtHandOff.Add(storage.PublishedMessages.ContainsKey(message.StorageId));
            CommittedMessages.Add(message);
        }

        public void EnqueueCommittedDelayedMessage(MediumMessage message)
        {
            VisibleAtHandOff.Add(storage.PublishedMessages.ContainsKey(message.StorageId));
            CommittedDelayedMessages.Add(message);
        }

        public ValueTask EnqueueToPublish(MediumMessage message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Coordinated capture must not dispatch through the transport path.");
        }

        public ValueTask EnqueueToExecute(
            MediumMessage message,
            ConsumerExecutorDescriptor? descriptor = null,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.CompletedTask;
        }

        public Task EnqueueToScheduler(
            MediumMessage message,
            DateTimeOffset publishTime,
            DbTransaction? transaction = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new InvalidOperationException("Coordinated capture must not schedule through the transport path.");
        }

        public ValueTask StartAsync(CancellationToken stoppingToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
