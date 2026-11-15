// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

/// <summary>
/// A store batch holds the rows of a run of enlisted publishes and writes them together on its flush, in publish
/// order, while a publish that middleware wraps, or that the storage cannot batch, is stored on its own after the rows
/// held before it.
/// </summary>
public sealed class OutboxStoreBatchTests : TestBase
{
    [Fact]
    public async Task should_write_held_rows_in_publish_order_in_one_store_when_flushed()
    {
        // given
        var harness = _CreateHarness();
        await using var unitOfWork = harness.CreateUnit();
        var batch = new OutboxStoreBatch(harness.Publisher, unitOfWork);

        // when
        var receipts = new List<PublishReceipt>();
        for (var i = 0; i < 3; i++)
        {
            receipts.Add(await batch.PublishAsync(new BatchMessage($"m{i}"), options: null, AbortToken));
        }

        var heldBeforeFlush = harness.Log.Count;
        await batch.FlushAsync(AbortToken);
        await unitOfWork.CompleteAsync(AbortToken);

        // then
        heldBeforeFlush.Should().Be(0, "a held row is written only by the flush");
        harness.Log.Should().Equal("batch:3");
        harness
            .BatchedRows.Select(row => row.Message.StorageId)
            .Should()
            .Equal(receipts.Select(r => r.StorageId!.Value));
        harness.BatchedTransactions.Should().ContainSingle().Which.Should().BeSameAs(harness.Transaction);
        harness.Dispatcher.CommittedMessages.Should().Equal(harness.BatchedRows.Select(row => row.Message));
    }

    [Fact]
    public async Task should_flush_held_rows_before_a_publish_that_middleware_wraps()
    {
        // given — middleware registered straight into DI for one message type only, so the other publishes may be
        // held
        var harness = _CreateHarness(services =>
            services.AddScoped<IPublishMiddleware<PublishContext<WrappedMessage>>, RecordingMiddleware>()
        );
        await using var unitOfWork = harness.CreateUnit();
        var batch = new OutboxStoreBatch(harness.Publisher, unitOfWork);

        // when
        await batch.PublishAsync(new BatchMessage("a"), options: null, AbortToken);
        await batch.PublishAsync(new BatchMessage("b"), options: null, AbortToken);
        await batch.PublishAsync(new WrappedMessage("c"), options: null, AbortToken);
        await batch.PublishAsync(new BatchMessage("d"), options: null, AbortToken);
        await batch.FlushAsync(AbortToken);
        await unitOfWork.CompleteAsync(AbortToken);

        // then — the held rows land before the middleware runs, so it sees only its own store
        harness.Log.Should().Equal("batch:2", "middleware:before", "single", "middleware:after", "batch:1");
        harness
            .Dispatcher.CommittedMessages.Select(message =>
                message.Origin.Value switch
                {
                    BatchMessage batched => batched.Value,
                    WrappedMessage wrapped => wrapped.Value,
                    _ => null,
                }
            )
            .Should()
            .Equal("a", "b", "c", "d");
    }

    [Fact]
    public async Task should_store_every_row_on_its_own_when_the_builder_registered_publish_middleware_on_the_lane()
    {
        // given — the builder registers middleware for another message type; the pipeline then treats every publish
        // on the lane as one middleware may wrap, so none is held
        var harness = _CreateHarness(services =>
            new MessagingBuilder(services).AddPublishMiddlewareFor<RecordingMiddleware, WrappedMessage>(MessageLane.Bus)
        );
        await using var unitOfWork = harness.CreateUnit();
        var batch = new OutboxStoreBatch(harness.Publisher, unitOfWork);

        // when
        await batch.PublishAsync(new BatchMessage("a"), options: null, AbortToken);
        await batch.PublishAsync(new BatchMessage("b"), options: null, AbortToken);
        await batch.FlushAsync(AbortToken);

        // then
        harness.Log.Should().Equal("single", "single");
    }

    [Fact]
    public async Task should_surface_the_store_error_from_the_flush_and_hand_off_no_row()
    {
        // given
        var failure = new InvalidOperationException("duplicate key");
        var harness = _CreateHarness(batchFailure: failure);
        await using var unitOfWork = harness.CreateUnit();
        var batch = new OutboxStoreBatch(harness.Publisher, unitOfWork);
        await batch.PublishAsync(new BatchMessage("a"), options: null, AbortToken);

        // when
        var act = async () => await batch.FlushAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should()
            .BeSameAs(failure);
        await unitOfWork.CompleteAsync(AbortToken);
        harness.Dispatcher.CommittedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_drop_held_rows_when_disposed_without_a_flush()
    {
        // given
        var harness = _CreateHarness();
        await using var unitOfWork = harness.CreateUnit();
        var batch = new OutboxStoreBatch(harness.Publisher, unitOfWork);
        await batch.PublishAsync(new BatchMessage("a"), options: null, AbortToken);

        // when
        await batch.DisposeAsync();
        await batch.FlushAsync(AbortToken);
        await unitOfWork.CompleteAsync(AbortToken);

        // then
        harness.Log.Should().BeEmpty();
        harness.Dispatcher.CommittedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_store_each_row_on_its_own_when_the_storage_cannot_batch()
    {
        // given
        var harness = _CreateHarness(storageBatches: false);
        await using var unitOfWork = harness.CreateUnit();
        var batch = new OutboxStoreBatch(harness.Publisher, unitOfWork);

        // when
        await batch.PublishAsync(new BatchMessage("a"), options: null, AbortToken);
        await batch.PublishAsync(new BatchMessage("b"), options: null, AbortToken);
        await batch.FlushAsync(AbortToken);

        // then
        harness.Log.Should().Equal("single", "single");
    }

    #region Harness

    private static Harness _CreateHarness(
        Action<ServiceCollection>? configureServices = null,
        Exception? batchFailure = null,
        bool storageBatches = true
    )
    {
        var log = new List<string>();
        var batchedRows = new List<(string Name, MediumMessage Message)>();
        var batchedTransactions = new List<DbTransaction>();
        var storage = storageBatches
            ? Substitute.For<IDataStorage, IBatchPublishedMessageStore>()
            : Substitute.For<IDataStorage>();
        storage
            .StoreMessageAsync(
                Arg.Any<string>(),
                Arg.Any<MediumMessage>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                log.Add("single");
                return ValueTask.FromResult(_Stored(call.ArgAt<MediumMessage>(1)));
            });

        if (storage is IBatchPublishedMessageStore batchStore)
        {
            batchStore
                .CreatePublishedMessage(Arg.Any<MediumMessage>(), Arg.Any<DateTimeOffset?>())
                .Returns(call => _Stored(call.ArgAt<MediumMessage>(0)));
            batchStore
                .StorePublishedMessagesAsync(
                    Arg.Any<IReadOnlyList<(string Name, MediumMessage Message)>>(),
                    Arg.Any<DbTransaction>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                {
                    if (batchFailure is not null)
                    {
                        return ValueTask.FromException(batchFailure);
                    }

                    var rows = call.ArgAt<IReadOnlyList<(string Name, MediumMessage Message)>>(0);
                    log.Add($"batch:{rows.Count}");
                    batchedRows.AddRange(rows);
                    batchedTransactions.Add(call.ArgAt<DbTransaction>(1));
                    return ValueTask.CompletedTask;
                });
        }

        var services = new ServiceCollection();
        services.AddSingleton(log);
        configureServices?.Invoke(services);
        var provider = services.BuildServiceProvider();
        var pipeline = new PublishMiddlewarePipeline(provider, provider.GetService<IMiddlewareDescriptorRegistry>());

        var dispatcher = new RecordingCommittedDispatcher();
        var optionsAccessor = Options.Create(new MessagingOptions());
        var writer = new OutboxMessageWriter(
            storage,
            dispatcher,
            new JsonUtf8Serializer(optionsAccessor),
            TimeProvider.System
        );
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(BatchMessage), "batch.message");
        registry.RegisterMessageName(typeof(WrappedMessage), "wrapped.message");
        var publishRequestFactory = new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            optionsAccessor,
            registry,
            new NullCurrentTenant()
        );
        var capabilities = MessagingCapabilityModel.Compose([
            MessagingProviderCapabilities.Transport(
                "TestTransport",
                [MessageLane.Bus],
                supportsIndependentLaneTopology: true
            ),
            MessagingProviderCapabilities.Storage(
                "TestStorage",
                [MessageLane.Bus],
                supportsDelayedScheduling: true,
                inboxGuarantee: InboxGuarantee.Transactional
            ),
        ]);
        var publisher = new MessagePublisher(
            new JsonUtf8Serializer(optionsAccessor),
            _ => throw new InvalidOperationException("An enlisted publish never sends directly."),
            publishRequestFactory,
            pipeline,
            TimeProvider.System,
            capabilities,
            static () => new RelationalCoordinationResolver(),
            () => writer
        );

        return new Harness(publisher, dispatcher, new TestDbTransaction(), log, batchedRows, batchedTransactions);
    }

    private static MediumMessage _Stored(MediumMessage envelope)
    {
        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = envelope.Origin,
            Content = "{}",
            Lane = envelope.Lane,
            Added = DateTimeOffset.UtcNow,
        };
    }

    private sealed record Harness(
        MessagePublisher Publisher,
        RecordingCommittedDispatcher Dispatcher,
        TestDbTransaction Transaction,
        List<string> Log,
        List<(string Name, MediumMessage Message)> BatchedRows,
        List<DbTransaction> BatchedTransactions
    )
    {
        public FakeUnitOfWork CreateUnit()
        {
            var resource = Substitute.For<IRelationalUnitOfWorkResource>();
            resource.Transaction.Returns(Transaction);

            return new FakeUnitOfWork { Resource = resource };
        }
    }

    private sealed record BatchMessage(string Value);

    private sealed record WrappedMessage(string Value);

    private sealed class RecordingMiddleware(List<string> log) : IPublishMiddleware<PublishContext<WrappedMessage>>
    {
        public async ValueTask InvokeAsync(PublishContext<WrappedMessage> context, Func<ValueTask> next)
        {
            log.Add("middleware:before");
            await next();
            log.Add("middleware:after");
        }
    }

    private sealed class RelationalCoordinationResolver : IDeliveryCoordinationResolver
    {
        public DeliveryCoordination Resolve(IUnitOfWork unitOfWork)
        {
            return unitOfWork.Resource is IRelationalUnitOfWorkResource { Transaction: { } transaction }
                ? DeliveryCoordination.Compatible(unitOfWork, transaction)
                : DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.MissingRelationalCapability);
        }
    }

    private sealed class RecordingCommittedDispatcher : IDispatcher, ICommittedMessageDispatcher
    {
        public List<MediumMessage> CommittedMessages { get; } = [];

        public ValueTask EnqueueCommittedMessageAsync(MediumMessage message, long waitStartedAt)
        {
            CommittedMessages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask EnqueueToPublish(MediumMessage message, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask EnqueueToExecute(
            MediumMessage message,
            ConsumerExecutorDescriptor? descriptor = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public Task EnqueueToScheduler(
            MediumMessage message,
            DateTimeOffset publishTime,
            DbTransaction? transaction = null,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public ValueTask StartAsync(CancellationToken stoppingToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestDbTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection? DbConnection => null;

        public override void Commit() { }

        public override void Rollback() { }
    }

    #endregion
}
