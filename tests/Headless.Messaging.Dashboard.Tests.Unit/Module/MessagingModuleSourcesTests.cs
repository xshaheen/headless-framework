// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
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
                Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>())
                    .ContinueWith(static _ => new MessageCount(0, false), TaskScheduler.Default)
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
    public async Task should_stream_an_outbox_change_under_the_outbox_name()
    {
        // given
        await using var services = _MainStore(out var primary);
        var storageId = Guid.NewGuid();
        var markedAt = _time.GetUtcNow();
        var outbox = Substitute.For<IDataStorage>();
        var monitoring = Substitute.For<IMonitoringApi>();
        var row = MessagingRows.Medium("invoice-2");
        row.StorageId = storageId;
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
                ],
                Cursor = call.Arg<MessageChangeCursor>(),
            });
        monitoring
            .GetPublishedMessagesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([row]);
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
        var cursor = await module.GetStartCursorAsync(AbortToken);

        // when
        var batch = await module.ReadChangesAsync(cursor, AbortToken);

        // then
        batch.UnavailableSources.Should().BeEmpty();
        var change = batch.Changes.Should().ContainSingle().Subject;
        change.Target.Id.Should().Be($"node-a~billing~{storageId:D}");
        change.Target.Kind.Should().Be("published");
        change.Version.Should().Be(markedAt.UtcTicks.ToString("D19", CultureInfo.InvariantCulture));
        change.Projection!.Value.GetProperty("store").GetString().Should().Be(_Outbox);
    }

    [Fact]
    public async Task should_publish_a_row_a_pipeline_wrote_on_this_node_with_its_change_marker_as_version()
    {
        // given
        await using var services = _MainStore(out var primary);
        var failed = await MessagingRows.ReceivedFailureAsync(primary, "r-7", cancellationToken: AbortToken);
        var events = new RecordingEventPublisher();
        await using var publisher = new MessagingChangePublisher(
            _Stores(primary),
            events,
            NullLogger<MessagingChangePublisher>.Instance
        );

        // when: the consumer pipeline reports the write it made
        publisher.OnStateWritten(MessageType.Subscribe, failed);

        // then
        var change = await _WaitForAsync(events, failed.StorageId);
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

    private static async Task<DashboardChange> _WaitForAsync(RecordingEventPublisher events, Guid storageId)
    {
        var limit = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < limit)
        {
            if (
                events.Changes.FirstOrDefault(c =>
                    c.Target.Id.EndsWith(storageId.ToString("D"), StringComparison.Ordinal)
                ) is
                { } change
            )
            {
                return change;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"No change for {storageId} was published.");
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
}
