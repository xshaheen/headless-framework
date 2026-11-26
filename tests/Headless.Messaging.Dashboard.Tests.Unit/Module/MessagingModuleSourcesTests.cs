// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests.Module;

/// <summary>
/// The module's reads over a store set of the in-memory main store and one additional outbox, named <c>billing</c>:
/// counts, Needs you, lookup, and change reads answer for the stores that answer and name the ones that do not.
/// </summary>
public sealed class MessagingModuleSourcesTests : TestBase
{
    private const string _Outbox = "billing";
    private const string _TraceId = "order-7f3a";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly MessagingMetricsEventListener _metrics = new();

    protected override ValueTask DisposeAsyncCore()
    {
        _metrics.Dispose();

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_report_a_failing_outbox_unavailable_and_keep_the_main_store_counts_current()
    {
        // given
        await using var services = _MainStore(out var primary);
        await MessagingRows.PublishedAsync(primary, "p-1", StatusName.Succeeded, AbortToken);
        await MessagingRows.ReceivedFailureAsync(primary, "r-1", cancellationToken: AbortToken);
        var module = _Module(primary, _FailingOutbox());

        // when
        var counts = await module.GetCountsAsync(_CountsRequest(), AbortToken);

        // then
        counts[MessagingCounts.PublishedAll].Count.Should().Be(1);
        counts[MessagingCounts.PublishedAll].UnavailableSources.Should().Equal(_Outbox);
        counts[MessagingCounts.PublishedSucceeded].Count.Should().Be(1);
        counts[MessagingCounts.ReceivedFailed].Count.Should().Be(1);
        counts[MessagingCounts.ReceivedFailed]
            .UnavailableSources.Should()
            .BeEmpty("received rows live in the main store");
    }

    [Fact]
    public async Task should_report_a_slow_outbox_unavailable_after_the_store_timeout()
    {
        // given
        await using var services = _MainStore(out var primary);
        await MessagingRows.PublishedAsync(primary, "p-1", StatusName.Succeeded, AbortToken);
        var hanging = Substitute.For<IDataStorage>();
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring
            .CountMessagesAsync(Arg.Any<MessageListQuery>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<MessageCount>(
                HangingReads.NeverAnswerAsync<MessageCount>(call.Arg<CancellationToken>())
            ));
        hanging.GetMonitoringApi().Returns(monitoring);
        var module = _Module(primary, hanging, storeTimeout: TimeSpan.FromMilliseconds(200));

        // when
        var counts = await module
            .GetCountsAsync(_CountsRequest(), AbortToken)
            .WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        counts[MessagingCounts.PublishedAll].Count.Should().Be(1);
        counts[MessagingCounts.PublishedAll].UnavailableSources.Should().Equal(_Outbox);
    }

    [Fact]
    public async Task should_list_one_needs_you_item_per_fingerprint_and_consumer_and_name_a_failing_outbox()
    {
        // given
        await using var services = _MainStore(out var primary);
        await MessagingRows.ReceivedFailureAsync(primary, "r-1", "v1:declined", cancellationToken: AbortToken);
        await MessagingRows.ReceivedFailureAsync(primary, "r-2", "v1:declined", cancellationToken: AbortToken);
        await MessagingRows.ReceivedFailureAsync(primary, "r-3", "v1:timeout", cancellationToken: AbortToken);
        var module = _Module(primary, _FailingOutbox());

        // when
        var answer = await ((IDashboardAttentionSource)module).GetAttentionAnswerAsync(
            new DashboardAttentionRequest { User = new() },
            AbortToken
        );

        // then
        answer.UnavailableSources.Should().Equal(_Outbox);
        answer.Items.Should().HaveCount(2);
        var declined = answer
            .Items.Should()
            .ContainSingle(item => item.GroupKey.Contains("v1:declined", StringComparison.Ordinal))
            .Subject;
        declined.Count.Should().Be(2);
        declined.Severity.Should().Be(DashboardAttentionSeverity.Critical);
        declined.Target!.Id.Should().Be(MessagingRows.Consumer);
        declined.GroupFilter!.Value.GetProperty("fingerprint").GetString().Should().Be("v1:declined");
        declined.GroupFilter!.Value.GetProperty("store").GetString().Should().Be("primary");
    }

    [Fact]
    public async Task should_resolve_an_outbox_row_with_its_store_name_in_the_id()
    {
        // given
        await using var services = _MainStore(out var primary);
        var storageId = Guid.NewGuid();
        var outbox = Substitute.For<IDataStorage>();
        var monitoring = Substitute.For<IMonitoringApi>();
        var row = MessagingRows.Medium("invoice-1");
        row.StorageId = storageId;
        row.StatusName = StatusName.Succeeded;
        monitoring.GetPublishedMessageAsync(storageId, Arg.Any<CancellationToken>()).Returns(row);
        monitoring
            .ListMessagesAsync(
                Arg.Any<MessageListQuery>(),
                Arg.Any<MessageListPosition?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new MessageListPage { Items = [] });
        outbox.GetMonitoringApi().Returns(monitoring);
        var module = _Module(primary, outbox);

        // when
        var bare = await module.ResolveAsync(
            new DashboardResolveRequest { Query = storageId.ToString(), User = new() },
            AbortToken
        );
        var full = await module.ResolveAsync(
            new DashboardResolveRequest { Query = $"node-a~billing~{storageId:D}", User = new() },
            AbortToken
        );

        // then
        var found = bare.Should().ContainSingle().Subject;
        found.Target.Kind.Should().Be("published");
        found.Target.Id.Should().Be($"node-a~billing~{storageId:D}");
        found.Title.Should().Contain(_Outbox);
        full.Should().ContainSingle().Which.Target.Id.Should().Be(found.Target.Id);
    }

    [Fact]
    public async Task should_stream_main_store_changes_while_an_outbox_fails_and_keep_the_outbox_position()
    {
        // given
        await using var services = _MainStore(out var primary);
        var module = _Module(primary, _FailingOutbox());
        var cursor = await module.GetStartCursorAsync(AbortToken);
        _time.Advance(TimeSpan.FromSeconds(1));
        var failed = await MessagingRows.ReceivedFailureAsync(primary, "r-1", cancellationToken: AbortToken);

        // when
        var batch = await module.ReadChangesAsync(cursor, AbortToken);

        // then
        batch.UnavailableSources.Should().Equal(_Outbox);
        var change = batch
            .Changes.Should()
            .ContainSingle(c => c.Target.Id == $"node-a~primary~{failed.StorageId:D}")
            .Subject;
        change.Target.Kind.Should().Be("received");
        change.Target.Node.Should().BeNull("every node reads the shared store, so the change goes to the plain topic");
        change.Projection!.Value.GetProperty("status").GetString().Should().Be("Failed");
        change.AffectsAttention.Should().BeTrue();
        batch.Cursor.Should().Contain("|billing=-", "a store that never answered starts once it does");
    }

    [Fact]
    public async Task should_stream_outbox_changes_under_the_outbox_name_with_one_projection_read()
    {
        // given
        await using var services = _MainStore(out var primary);
        var storageId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var markedAt = _time.GetUtcNow();
        var outbox = Substitute.For<IDataStorage>();
        var monitoring = Substitute.For<IMonitoringApi, IStorageIdMonitoringApi>();
        var exact = (IStorageIdMonitoringApi)monitoring;
        outbox
            .GetMessageChangeStartCursorAsync(Arg.Any<CancellationToken>())
            .Returns(MessageChangeCursor.StartAt(null));
        outbox
            .GetMessageChangesAsync(
                Arg.Any<MessageChangeCursor>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new MessageChangePage
            {
                Changes =
                [
                    new MessageChange
                    {
                        Kind = MessageChangeKind.Published,
                        Id = storageId,
                        ChangedAt = markedAt,
                        Status = StatusName.Failed,
                    },
                    new MessageChange
                    {
                        Kind = MessageChangeKind.Published,
                        Id = otherId,
                        ChangedAt = markedAt.AddTicks(1),
                        Status = StatusName.Succeeded,
                    },
                ],
                Cursor = call.Arg<MessageChangeCursor>(),
            });
        exact
            .GetMessageViewsAsync(MessageType.Publish, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([
                new StoredMessageView(
                    new MessageView
                    {
                        StorageId = storageId,
                        MessageId = "invoice-2",
                        Version = "v1",
                        Name = "invoice.sent",
                        StatusName = StatusName.Failed,
                    },
                    "InvalidOperationException: broker refused"
                ),
                new StoredMessageView(
                    new MessageView
                    {
                        StorageId = otherId,
                        MessageId = "invoice-3",
                        Version = "v1",
                        Name = "invoice.sent",
                        StatusName = StatusName.Succeeded,
                    },
                    null
                ),
            ]);
        outbox.GetMonitoringApi().Returns(monitoring);
        var module = _Module(primary, outbox, storeTimeout: Timeout.InfiniteTimeSpan);
        var cursor = await module.GetStartCursorAsync(AbortToken);

        // when
        var batch = await module.ReadChangesAsync(cursor, AbortToken);

        // then
        batch.UnavailableSources.Should().BeEmpty();
        batch
            .Changes.Select(static item => item.Target.Id)
            .Should()
            .Equal($"node-a~billing~{storageId:D}", $"node-a~billing~{otherId:D}");
        var change = batch.Changes[0];
        change.Target.Kind.Should().Be("published");
        change.Version.Should().Be(markedAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture));
        var projection = change.Projection!.Value;
        projection.GetProperty("store").GetString().Should().Be(_Outbox);
        projection.GetProperty("reason").GetString().Should().Be("InvalidOperationException: broker refused");
        await exact
            .Received(1)
            .GetMessageViewsAsync(
                MessageType.Publish,
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2 && ids[0] == storageId && ids[1] == otherId),
                Arg.Any<CancellationToken>()
            );
        await monitoring
            .DidNotReceive()
            .GetPublishedMessagesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
        await monitoring
            .DidNotReceive()
            .ListMessagesAsync(
                Arg.Any<MessageListQuery>(),
                Arg.Any<MessageListPosition?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task should_follow_every_custom_provider_page_until_it_finds_the_exact_storage_id(
        bool exists,
        bool hasMessageId
    )
    {
        var storage = Substitute.For<IDataStorage>();
        var monitoring = Substitute.For<IMonitoringApi>();
        storage.GetMonitoringApi().Returns(monitoring);
        var store = new MessagingStore("primary", storage, HoldsReceived: true);
        var newer = new MessageView
        {
            StorageId = Guid.NewGuid(),
            MessageId = "invoice-2",
            Name = "invoice.sent",
            Version = "v1",
            StatusName = StatusName.Succeeded,
        };
        var older = new MessageView
        {
            StorageId = Guid.NewGuid(),
            MessageId = newer.MessageId,
            Name = newer.Name,
            Version = newer.Version,
            StatusName = StatusName.Failed,
            IsHeld = true,
        };
        var continuation = MessageListPosition.Of(newer);
        var messageId = hasMessageId ? older.MessageId : null;
        var query = new MessageListQuery { MessageType = MessageType.Subscribe, MessageId = messageId };
        monitoring
            .ListMessagesAsync(query, after: null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new MessageListPage { Items = [newer], Next = continuation });
        monitoring
            .ListMessagesAsync(query, continuation, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new MessageListPage { Items = exists ? [older] : [] });

        var found = await MessagingViews.ReadViewAsync(
            store,
            MessageType.Subscribe,
            older.StorageId,
            messageId,
            AbortToken
        );

        found.Should().BeSameAs(exists ? older : null);
    }

    [Fact]
    public async Task should_publish_a_row_a_pipeline_wrote_on_this_node_with_its_change_marker_as_version()
    {
        // given
        await using var services = _MainStore(out var primary);
        var failed = await MessagingRows.ReceivedFailureAsync(primary, "r-7", cancellationToken: AbortToken);
        var events = new NotifyingEventPublisher();
        await using var publisher = new MessagingChangePublisher(
            _Stores(primary),
            events,
            NullLogger<MessagingChangePublisher>.Instance
        );

        // when: the consumer pipeline reports the write it made
        publisher.OnStateWritten(MessageType.Subscribe, failed);

        // then
        var change = await events.WaitForAsync(failed.StorageId).Bounded();
        change.Target.Kind.Should().Be("received");
        change.Projection!.Value.GetProperty("status").GetString().Should().Be("Failed");
        var stored = (
            await primary
                .GetMonitoringApi()
                .ListMessagesAsync(
                    new MessageListQuery { MessageType = MessageType.Subscribe, MessageId = "r-7" },
                    after: null,
                    pageSize: 10,
                    AbortToken
                )
        )
            .Items.Should()
            .ContainSingle()
            .Subject;
        change.Version.Should().Be(stored.ChangedAt!.Value.UtcTicks.ToString("D19", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task should_dispose_the_host_without_throwing_after_a_pipeline_reported_a_state_write()
    {
        // given: a host whose pipelines reach the publisher through the observer registration
        var events = new NotifyingEventPublisher();
        var app = await MessagingModuleTestHost.StartAsync(
            _time,
            AbortToken,
            configure: services => services.AddSingleton<IDashboardEventPublisher>(events)
        );
        var storage = app.Services.GetRequiredService<IDataStorage>();
        var failed = await MessagingRows.ReceivedFailureAsync(storage, "r-8", cancellationToken: AbortToken);
        var observer = app.Services.GetServices<IMessageStateObserver>().Should().ContainSingle().Subject;
        observer.OnStateWritten(MessageType.Subscribe, failed);
        await events.WaitForAsync(failed.StorageId).Bounded();

        // when: the container disposes the publisher once per registration that returned it
        var dispose = async () => await app.DisposeAsync();

        // then
        await dispose.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_dispose_the_change_publisher_twice_without_throwing_after_its_reader_started()
    {
        // given
        await using var services = _MainStore(out var primary);
        var failed = await MessagingRows.ReceivedFailureAsync(primary, "r-9", cancellationToken: AbortToken);
        var events = new NotifyingEventPublisher();
        await using var publisher = new MessagingChangePublisher(
            _Stores(primary),
            events,
            NullLogger<MessagingChangePublisher>.Instance
        );
        publisher.OnStateWritten(MessageType.Subscribe, failed);
        await events.WaitForAsync(failed.StorageId).Bounded();
        await publisher.DisposeAsync();

        // when
        var again = async () => await publisher.DisposeAsync();

        // then
        await again.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_map_a_published_message_and_its_two_generations_to_three_items_with_their_causation_keys()
    {
        // given: a message a job run published, delivered to two consumers
        await using var services = _MainStore(out var primary);
        var runId = Guid.NewGuid().ToString("D");
        var published = await primary.StoreMessageAsync(
            MessagingRows.Name,
            _Correlated("m-1", causationId: runId),
            cancellationToken: AbortToken
        );
        await primary.ChangePublishStateAsync(published, StatusName.Succeeded, cancellationToken: AbortToken);
        var first = await primary.StoreReceivedMessageAsync(
            MessagingRows.Name,
            "billing.charge",
            _Correlated("m-1", causationId: runId),
            AbortToken
        );
        var second = await primary.StoreReceivedMessageAsync(
            MessagingRows.Name,
            "billing.audit",
            _Correlated("m-1", causationId: runId),
            AbortToken
        );
        var module = _Module(primary);

        // when
        var answer = await module.GetTraceAsync(_TraceRequest(), AbortToken);

        // then
        answer.Items.Should().HaveCount(3);
        answer.Limited.Should().BeFalse();
        answer.UnavailableSources.Should().BeEmpty();
        var message = answer
            .Items.Should()
            .ContainSingle(item => string.Equals(item.Target.Kind, "published", StringComparison.Ordinal))
            .Subject;
        message.Target.Id.Should().Be($"node-a~primary~{published.StorageId:D}");
        message.Key.Should().Be(new DashboardTraceKey { Kind = DashboardTraceKeyKind.Message, Value = "m-1" });
        message.Causation.Should().Be(new DashboardTraceKey { Kind = DashboardTraceKeyKind.Run, Value = runId });
        message.DeliversMessageId.Should().BeNull();
        message.Status.Should().Be("Succeeded");
        message.StatusRole.Should().Be("ok");
        message.CorrelationId.Should().Be(_TraceId);
        message.Node.Should().Be("node-a");
        var generations = answer
            .Items.Where(item => string.Equals(item.Target.Kind, "received", StringComparison.Ordinal))
            .ToList();
        generations
            .Select(item => item.Key)
            .Should()
            .BeEquivalentTo([
                new DashboardTraceKey { Kind = DashboardTraceKeyKind.Delivery, Value = first.StorageId.ToString("D") },
                new DashboardTraceKey { Kind = DashboardTraceKeyKind.Delivery, Value = second.StorageId.ToString("D") },
            ]);
        generations.Should().OnlyContain(item => item.DeliversMessageId == "m-1");
        generations
            .Should()
            .OnlyContain(item =>
                item.Causation == new DashboardTraceKey { Kind = DashboardTraceKeyKind.Run, Value = runId }
            );
        answer.ListLink!.Path.Should().Be("published");
        answer.ListLink.State!.Value.GetProperty("correlationId").GetString().Should().Be(_TraceId);
    }

    [Fact]
    public async Task should_key_a_message_published_inside_a_consumer_to_the_message_it_consumed()
    {
        // given: m-2 was published by a consumer of m-1, which this store holds only as a received generation
        await using var services = _MainStore(out var primary);
        await primary.StoreReceivedMessageAsync(
            MessagingRows.Name,
            MessagingRows.Consumer,
            _Correlated("m-1", causationId: null),
            AbortToken
        );
        await primary.StoreMessageAsync(
            MessagingRows.Name,
            _Correlated("m-2", causationId: "m-1"),
            cancellationToken: AbortToken
        );
        var module = _Module(primary);

        // when
        var answer = await module.GetTraceAsync(_TraceRequest(), AbortToken);

        // then
        answer
            .Items.Single(item => string.Equals(item.Target.Kind, "published", StringComparison.Ordinal))
            .Causation.Should()
            .Be(new DashboardTraceKey { Kind = DashboardTraceKeyKind.Message, Value = "m-1" });
    }

    [Fact]
    public async Task should_show_a_pending_scheduled_delivery_with_its_due_time()
    {
        // given
        await using var services = _MainStore(out var primary);
        var dueAt = _time.GetUtcNow().AddHours(3);
        var scheduled = await primary.StoreScheduledMessageAsync(
            MessagingRows.Name,
            _Correlated("m-later", causationId: null),
            dueAt,
            cancellationToken: AbortToken
        );
        var module = _Module(primary);

        // when
        var answer = await module.GetTraceAsync(_TraceRequest(), AbortToken);

        // then
        var item = answer.Items.Should().ContainSingle().Subject;
        item.Target.Id.Should().Be($"node-a~primary~{scheduled.StorageId:D}");
        item.Status.Should().Be("Delayed");
        item.StatusRole.Should().Be("sched");
        item.TimestampKind.Should().Be(DashboardTraceTimestampKind.Due);
        item.At.Should().Be(dueAt);
    }

    [Fact]
    public async Task should_list_the_oldest_500_items_and_set_limited_when_two_stores_each_return_400_rows()
    {
        // given: the main store holds the even minutes and the outbox the odd ones
        var start = _time.GetUtcNow();
        var primary = _TraceStore(Enumerable.Range(0, 400).Select(i => _Row(start.AddMinutes(i * 2))));
        var outbox = _TraceStore(Enumerable.Range(0, 400).Select(i => _Row(start.AddMinutes((i * 2) + 1))));
        var module = _Module(primary, outbox);

        // when
        var answer = await module.GetTraceAsync(_TraceRequest(), AbortToken);

        // then
        answer.Items.Should().HaveCount(500);
        answer.Limited.Should().BeTrue();
        answer
            .Items.Should()
            .Equal(Enumerable.Range(0, 500).Select(i => start.AddMinutes(i)), (item, at) => item.At == at);
    }

    [Fact]
    public async Task should_name_a_custom_store_without_the_lookup_unavailable_as_not_supported()
    {
        // given: the outbox's monitoring API keeps the lookup's default, which throws NotSupportedException
        await using var services = _MainStore(out var primary);
        await primary.StoreMessageAsync(
            MessagingRows.Name,
            _Correlated("m-1", causationId: null),
            cancellationToken: AbortToken
        );
        var outbox = Substitute.For<IDataStorage>();
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring
            .GetCorrelatedMessagesAsync(Arg.Any<MessageTraceQuery>(), Arg.Any<CancellationToken>())
            .Throws(
                new NotSupportedException(
                    "The monitoring API does not support reading the messages of a correlation id."
                )
            );
        outbox.GetMonitoringApi().Returns(monitoring);
        var module = _Module(primary, outbox);

        // when
        var answer = await module.GetTraceAsync(_TraceRequest(), AbortToken);

        // then
        answer.UnavailableSources.Should().Equal($"{_Outbox} (correlation lookup not supported)");
        answer.Items.Should().ContainSingle().Which.Key.Value.Should().Be("m-1");
    }

    // Completes a wait when the matching change is published, instead of polling the list of what was published.
    private sealed class NotifyingEventPublisher : IDashboardEventPublisher
    {
        private readonly Lock _lock = new();
        private readonly List<DashboardChange> _changes = [];
        private readonly List<(string Suffix, TaskCompletionSource<DashboardChange> Signal)> _waiters = [];

        public void Publish(DashboardChange change)
        {
            lock (_lock)
            {
                _changes.Add(change);

                foreach (var (suffix, signal) in _waiters)
                {
                    if (change.Target.Id.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        signal.TrySetResult(change);
                    }
                }
            }
        }

        /// <summary>Completes with the change published for the row, including one published before this call.</summary>
        public Task<DashboardChange> WaitForAsync(Guid storageId)
        {
            var suffix = storageId.ToString("D");

            lock (_lock)
            {
                var published = _changes.FirstOrDefault(c => c.Target.Id.EndsWith(suffix, StringComparison.Ordinal));

                if (published is not null)
                {
                    return Task.FromResult(published);
                }

                var signal = new TaskCompletionSource<DashboardChange>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                _waiters.Add((suffix, signal));

                return signal.Task;
            }
        }
    }

    private ServiceProvider _MainStore(out IDataStorage storage)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });
        var provider = services.BuildServiceProvider();
        storage = provider.GetRequiredService<IDataStorage>();

        return provider;
    }

    private static IDataStorage _FailingOutbox()
    {
        var outbox = Substitute.For<IDataStorage>();
        outbox.GetMonitoringApi().Throws(new InvalidOperationException("billing is down"));
        outbox
            .GetMessageChangeStartCursorAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("billing is down"));
        outbox
            .GetMessageChangesAsync(
                Arg.Any<MessageChangeCursor>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("billing is down"));

        return outbox;
    }

    private static MessagingStores _Stores(
        IDataStorage primary,
        IDataStorage? outbox = null,
        TimeSpan? storeTimeout = null
    )
    {
        MessagingStore[] stores = outbox is null
            ? [new("primary", primary, HoldsReceived: true)]
            : [new("primary", primary, HoldsReceived: true), new(_Outbox, outbox, HoldsReceived: false)];

        return new MessagingStores("node-a", stores, storeTimeout ?? TimeSpan.FromSeconds(5));
    }

    private MessagingModule _Module(IDataStorage primary, IDataStorage outbox, TimeSpan? storeTimeout = null)
    {
        return new MessagingModule(
            _Stores(primary, outbox, storeTimeout),
            _metrics,
            new MessagingDashboardOptions { NodeName = "node-a" },
            new ServiceCollection().BuildServiceProvider()
        );
    }

    private static DashboardCountsRequest _CountsRequest() => new() { User = new() };

    private static DashboardTraceRequest _TraceRequest() => new() { CorrelationId = _TraceId, User = new() };

    private MessagingModule _Module(IDataStorage primary)
    {
        return new MessagingModule(
            _Stores(primary),
            _metrics,
            new MessagingDashboardOptions { NodeName = "node-a" },
            new ServiceCollection().BuildServiceProvider()
        );
    }

    private static MediumMessage _Correlated(string messageId, string? causationId)
    {
        var message = MessagingRows.Medium(messageId);
        message.Origin.Headers[Headers.CorrelationId] = _TraceId;

        if (causationId is not null)
        {
            message.Origin.Headers[Headers.CausationId] = causationId;
        }

        return message;
    }

    private static MessageTraceRow _Row(DateTimeOffset added) =>
        new()
        {
            StorageId = Guid.NewGuid(),
            MessageId = Guid.NewGuid().ToString("D"),
            Name = MessagingRows.Name,
            Added = added,
            Status = StatusName.Succeeded,
        };

    // A store that answers the trace lookup as a real one does: its oldest limit + 1 rows.
    private static IDataStorage _TraceStore(IEnumerable<MessageTraceRow> published)
    {
        var rows = published.ToList();
        var storage = Substitute.For<IDataStorage>();
        var monitoring = Substitute.For<IMonitoringApi>();
        monitoring
            .GetCorrelatedMessagesAsync(Arg.Any<MessageTraceQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<MessageTraceResult>(
                new MessageTraceResult
                {
                    Published = [.. rows.Take(call.Arg<MessageTraceQuery>().Limit + 1)],
                    Received = [],
                }
            ));
        storage.GetMonitoringApi().Returns(monitoring);

        return storage;
    }
}
