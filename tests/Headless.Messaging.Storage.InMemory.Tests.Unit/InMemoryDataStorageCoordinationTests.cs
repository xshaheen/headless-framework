// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
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
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// In-memory storage takes part in a non-relational unit of work: rows written through the coordinated
/// publish path stay invisible until the unit completes and are discarded when it fails. A unit whose resource
/// is relational stays incompatible, because in-memory rows cannot be atomic with a database.
/// </summary>
public sealed class InMemoryDataStorageCoordinationTests : TestBase
{
    [Fact]
    public async Task should_report_resource_less_unit_of_work_compatible_without_transaction()
    {
        var storage = _CreateStorage();
        await using var unitOfWork = await _BeginUnitOfWorkAsync();

        var coordination = _Resolver(storage).Resolve(unitOfWork);

        coordination.Status.Should().Be(DeliveryCoordinationStatus.Compatible);
        coordination.UnitOfWork.Should().BeSameAs(unitOfWork);
        coordination.Transaction.Should().BeNull();
    }

    [Fact]
    public void should_report_relational_unit_of_work_incompatible()
    {
        var storage = _CreateStorage();
        var unitOfWork = _FakeRelationalUnitOfWork();

        var coordination = _Resolver(storage).Resolve(unitOfWork);

        coordination.Status.Should().Be(DeliveryCoordinationStatus.Incompatible);
        coordination.Mismatch.Should().Be(DeliveryCoordinationMismatch.StorageProvider);
    }

    [Fact]
    public async Task should_hide_coordinated_row_until_commit_then_expose_it()
    {
        var storage = _CreateStorage();
        var monitoring = new InMemoryMonitoringApi(storage, TimeProvider.System);
        await using var dispatcher = new RecordingCommittedDispatcher(storage);

        Guid storageId;
        await using (var unitOfWork = await _BeginUnitOfWorkAsync())
        {
            storageId = await _PublishCoordinatedAsync(storage, dispatcher, unitOfWork);

            (await monitoring.GetPublishedMessageAsync(storageId, AbortToken)).Should().BeNull();
            storage.PublishedMessages.Should().BeEmpty();
            dispatcher.CommittedMessages.Should().BeEmpty();

            await unitOfWork.CompleteAsync(AbortToken);
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

        await using (var unitOfWork = await _BeginUnitOfWorkAsync())
        {
            await _PublishCoordinatedAsync(storage, dispatcher, unitOfWork);
            await unitOfWork.RollbackAsync();
        }

        storage.PublishedMessages.Should().BeEmpty();
        dispatcher.CommittedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_leak_rows_between_sequential_units_of_work()
    {
        var storage = _CreateStorage();
        await using var dispatcher = new RecordingCommittedDispatcher(storage);

        await using (var rolledBack = await _BeginUnitOfWorkAsync())
        {
            await _PublishCoordinatedAsync(storage, dispatcher, rolledBack);
            await rolledBack.RollbackAsync();
        }

        Guid committedId;
        await using (var committed = await _BeginUnitOfWorkAsync())
        {
            committedId = await _PublishCoordinatedAsync(storage, dispatcher, committed);
            await committed.CompleteAsync(AbortToken);
        }

        storage.PublishedMessages.Keys.Should().Equal(committedId);
        dispatcher.CommittedMessages.Should().ContainSingle().Which.StorageId.Should().Be(committedId);
    }

    [Fact]
    public async Task should_expose_committed_row_before_dispatcher_hand_off()
    {
        // The storage buffer registers its completion callback when the row is captured, before the writer
        // obtains the outbox buffer; callbacks drain in registration order, so the dispatcher must already
        // see the row.
        var storage = _CreateStorage();
        await using var dispatcher = new RecordingCommittedDispatcher(storage);

        await using (var unitOfWork = await _BeginUnitOfWorkAsync())
        {
            await _PublishCoordinatedAsync(storage, dispatcher, unitOfWork);
            await _PublishCoordinatedAsync(storage, dispatcher, unitOfWork);
            await unitOfWork.CompleteAsync(AbortToken);
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
        await using (var unitOfWork = await _BeginUnitOfWorkAsync())
        {
            storageId = await _PublishCoordinatedAsync(
                storage,
                dispatcher,
                unitOfWork,
                delay: TimeSpan.FromMinutes(30)
            );

            storage.PublishedMessages.Should().BeEmpty();
            dispatcher.CommittedDelayedMessages.Should().BeEmpty();

            await unitOfWork.CompleteAsync(AbortToken);
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
        IUnitOfWork unitOfWork,
        TimeSpan? delay = null
    )
    {
        var writer = new OutboxMessageWriter(storage, dispatcher, TimeProvider.System);
        var request = _CreatePublishRequestFactory().Create(new CoordinatedMessage("value"), lane: MessageLane.Bus);
        var decision = DeliveryDecisionResolver.Resolve(
            MessageLane.Bus,
            DeliveryMode.Durable,
            TransactionEnlistment.Required,
            delay,
            _Resolver(storage).Resolve(unitOfWork),
            TimeProvider.System.GetUtcNow()
        );

        return await writer.WriteAsync(request, decision, AbortToken);
    }

    private static IDeliveryCoordinationResolver _Resolver(InMemoryDataStorage storage) => storage;

    private static async Task<IUnitOfWork> _BeginUnitOfWorkAsync()
    {
        var manager = _CreateUnitOfWorkManager();

        return await manager.BeginAsync();
    }

    private static IUnitOfWorkManager _CreateUnitOfWorkManager()
    {
        var services = new ServiceCollection();
        services.AddUnitOfWork();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // A resource-less unit of work has no scope-bound resource to release, so a manager resolved from its
        // own throwaway scope is sufficient for this test's lifetime — the scope's disposal is irrelevant once
        // the returned unit of work has been disposed.
#pragma warning disable CA2000 // The scope's disposal is irrelevant, per the note above.
        return provider.CreateAsyncScope().ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
#pragma warning restore CA2000
    }

    private static IUnitOfWork _FakeRelationalUnitOfWork()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.Resource.Returns(Substitute.For<IRelationalUnitOfWorkResource>());

        return unitOfWork;
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
