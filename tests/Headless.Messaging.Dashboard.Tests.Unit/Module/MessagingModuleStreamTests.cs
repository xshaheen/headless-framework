// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Tests.Module;

/// <summary>
/// The Messaging module's live changes: a failure written to the store reaches an open stream without a list request,
/// a failure written through another node over the same store reaches this node's stream through its own read of
/// the store, and a row whose transaction committed seconds after its write is still streamed.
/// </summary>
public sealed class MessagingModuleStreamTests : TestBase
{
    private static readonly TimeSpan _Step = TimeSpan.FromMilliseconds(50);

    // The shell's default change poll interval.
    private static readonly TimeSpan _ChangeInterval = TimeSpan.FromSeconds(2);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task should_stream_a_received_failure_on_the_serving_node_without_a_list_request()
    {
        // given
        await using var node = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        var storage = node.Services.GetRequiredService<IDataStorage>();
        using var client = node.GetTestClient();
        using var response = await _OpenStreamAsync(client);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(AbortToken));
        var stored = await storage.StoreReceivedMessageAsync(
            MessagingRows.Name,
            MessagingRows.Consumer,
            MessagingRows.Medium("m-1"),
            AbortToken
        );
#pragma warning disable CA2025 // False positive: the test awaits the read before the reader is disposed, and fails first if it never completes.
        var change = MessagingModuleTestHost.NextChangeAsync(reader, stored.StorageId, AbortToken);
#pragma warning restore CA2025

        // when
        var failedAt = _time.GetUtcNow();
        stored.FailureFingerprint = "v1:declined";
        await storage.ChangeReceiveStateAsync(stored, StatusName.Failed, cancellationToken: AbortToken);
        await _AdvanceUntilAsync(change);

        // then
        var json = await change;
        json.GetProperty("module").GetString().Should().Be("messaging");
        json.GetProperty("kind").GetString().Should().Be("received");
        json.GetProperty("id").GetString().Should().Be($"node-a~primary~{stored.StorageId:D}");
        json.GetProperty("projection").GetProperty("status").GetString().Should().Be("Failed");
        (_time.GetUtcNow() - failedAt).Should().BeLessThanOrEqualTo(_ChangeInterval + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_show_a_failure_written_through_node_b_in_a_stream_on_node_a_within_the_change_interval_plus_one_second()
    {
        // given: two nodes over one store, sharing nothing else
        await using var nodeA = await MessagingModuleTestHost.StartAsync(_time, AbortToken, nodeName: "node-a");
        var store = nodeA.Services.GetRequiredService<IDataStorage>();
        await using var nodeB = await MessagingModuleTestHost.StartAsync(
            _time,
            AbortToken,
            nodeName: "node-b",
            sharedStorage: store
        );
        var throughB = nodeB.Services.GetRequiredService<IDataStorage>();
        throughB.Should().BeSameAs(store);
        var stored = await throughB.StoreReceivedMessageAsync(
            MessagingRows.Name,
            MessagingRows.Consumer,
            MessagingRows.Medium("m-2"),
            AbortToken
        );

        using var client = nodeA.GetTestClient();
        using var response = await _OpenStreamAsync(client);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(AbortToken));
#pragma warning disable CA2025 // False positive: the test awaits the read before the reader is disposed, and fails first if it never completes.
        var change = MessagingModuleTestHost.NextChangeAsync(reader, stored.StorageId, AbortToken);
#pragma warning restore CA2025

        // when: node B fails the row; node A's own pipelines never hear of it
        var failedAt = _time.GetUtcNow();
        await throughB.ChangeReceiveStateAsync(stored, StatusName.Failed, cancellationToken: AbortToken);
        await _AdvanceUntilAsync(change);

        // then
        var json = await change;
        json.GetProperty("id").GetString().Should().Be($"node-a~primary~{stored.StorageId:D}");
        json.GetProperty("projection").GetProperty("status").GetString().Should().Be("Failed");
        (_time.GetUtcNow() - failedAt).Should().BeLessThanOrEqualTo(_ChangeInterval + TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(3, false)]
    public async Task should_stream_a_row_committed_four_seconds_after_its_write_only_inside_the_overlap_window(
        int overlapSeconds,
        bool streamed
    )
    {
        // given: a store whose rows become visible to a change read when their transaction commits, not when the
        // write stamped them
        var store = new LateCommitStore();
        var at = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        var stores = new MessagingStores(
            "node-a",
            [new("primary", store.Storage, HoldsReceived: true)],
            TimeSpan.FromSeconds(5)
        );
        var source = new MessagingChangeSource(stores, pageSize: 200, TimeSpan.FromSeconds(overlapSeconds));
        var cursor = await source.GetStartCursorAsync(AbortToken);
        var late = Guid.NewGuid();
        var changes = new List<DashboardChange>();

        // when: the late row is stamped at 12:00:01 and commits at 12:00:05, after rows stamped later were read
        store.Write(late, at.AddSeconds(1), visible: false);
        store.Write(Guid.NewGuid(), at.AddSeconds(2), visible: true);
        cursor = await _ReadAsync(source, cursor, changes);
        store.Write(Guid.NewGuid(), at.AddSeconds(4), visible: true);
        cursor = await _ReadAsync(source, cursor, changes);
        store.Commit(late);
        cursor = await _ReadAsync(source, cursor, changes);
        await _ReadAsync(source, cursor, changes);

        // then: repeats inside the window carry one version, so the shell sends the row once
        var lateChanges = changes
            .Where(c => c.Target.Id.EndsWith(late.ToString("D"), StringComparison.Ordinal))
            .ToArray();

        if (streamed)
        {
            lateChanges.Should().NotBeEmpty();
            lateChanges.Select(static c => c.Version).Distinct(StringComparer.Ordinal).Should().ContainSingle();
        }
        else
        {
            lateChanges.Should().BeEmpty("a row committed later than the window after its write is not streamed");
        }
    }

    private async Task<string> _ReadAsync(MessagingChangeSource source, string cursor, List<DashboardChange> changes)
    {
        var batch = await source.ReadChangesAsync(cursor, AbortToken);
        batch.UnavailableSources.Should().BeEmpty();
        changes.AddRange(batch.Changes);

        return batch.Cursor;
    }

    private async Task<HttpResponseMessage> _OpenStreamAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("/headless/api/stream?topics=messaging.received", UriKind.Relative)
        );
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, AbortToken);
        response.EnsureSuccessStatusCode();

        return response;
    }

    // Moves the shared clock in small steps, as real time would pass, until the change arrives or 15 s of real time pass.
    private async Task _AdvanceUntilAsync(Task change)
    {
        var limit = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (!change.IsCompleted && DateTime.UtcNow < limit)
        {
            _time.Advance(_Step);
            await Task.Delay(10, AbortToken);
        }

        change.IsCompleted.Should().BeTrue("the change should reach the stream");
    }

    /// <summary>
    /// A store that holds published rows with a change marker and a commit flag: a change read sees a row only once it
    /// committed, with the marker its write stamped, as a relational store does for a transaction that commits late.
    /// The storage's own change read, built on the two reads this fake answers, runs unchanged.
    /// </summary>
    private sealed class LateCommitStore
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<Guid, (DateTimeOffset ChangedAt, bool Visible)> _rows = [];

        public LateCommitStore()
        {
            var storage = Substitute.For<AbstractDataStorage>();
            var monitoring = Substitute.For<IMonitoringApi>();
            storage
                .GetLatestMessageChangeMarkerAsync(Arg.Any<CancellationToken>())
                .Returns(_ => ValueTask.FromResult(Latest()));
            storage
                .ReadChangedMessagesAsync(
                    Arg.Any<MessageChangeKind>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<MessageChangePosition?>(),
                    Arg.Any<int>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                    ValueTask.FromResult(
                        Read(
                            call.ArgAt<MessageChangeKind>(0),
                            call.ArgAt<DateTimeOffset>(1),
                            call.ArgAt<DateTimeOffset>(2),
                            call.ArgAt<MessageChangePosition?>(3),
                            call.ArgAt<int>(4)
                        )
                    )
                );
            // The pass logic under test is the storage's own: its default members, built on the two reads above, are
            // not declared on the substituted class, so they run unchanged.
            monitoring
                .GetPublishedMessagesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                    ValueTask.FromResult<IReadOnlyList<MediumMessage>>([.. call.Arg<IReadOnlyList<Guid>>().Select(Row)])
                );
            // No list row is read back, so the projection comes from the by-id read.
            monitoring
                .ListMessagesAsync(
                    Arg.Any<MessageListQuery>(),
                    Arg.Any<MessageListPosition?>(),
                    Arg.Any<int>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(new MessageListPage { Items = [] });
            storage.GetMonitoringApi().Returns(monitoring);
            Storage = storage;
        }

        public IDataStorage Storage { get; }

        public void Write(Guid id, DateTimeOffset changedAt, bool visible)
        {
            lock (_lock)
            {
                _rows[id] = (changedAt, visible);
            }
        }

        public void Commit(Guid id)
        {
            lock (_lock)
            {
                _rows[id] = _rows[id] with { Visible = true };
            }
        }

        public DateTimeOffset? Latest()
        {
            lock (_lock)
            {
                return _rows.Values.Where(static row => row.Visible).Max(static row => (DateTimeOffset?)row.ChangedAt);
            }
        }

        public IReadOnlyList<MessageChange> Read(
            MessageChangeKind kind,
            DateTimeOffset after,
            DateTimeOffset through,
            MessageChangePosition? resumeAfter,
            int maxCount
        )
        {
            if (kind != MessageChangeKind.Published)
            {
                return [];
            }

            lock (_lock)
            {
                return
                [
                    .. _rows
                        .Where(row =>
                            row.Value.Visible
                            && row.Value.ChangedAt > after
                            && row.Value.ChangedAt <= through
                            && (
                                resumeAfter is not { } position
                                || row.Value.ChangedAt > position.ChangedAt
                                || (row.Value.ChangedAt == position.ChangedAt && row.Key.CompareTo(position.Id) > 0)
                            )
                        )
                        .OrderBy(static row => row.Value.ChangedAt)
                        .ThenBy(static row => row.Key)
                        .Take(maxCount)
                        .Select(static row => new MessageChange
                        {
                            Kind = MessageChangeKind.Published,
                            Id = row.Key,
                            ChangedAt = row.Value.ChangedAt,
                            Status = StatusName.Succeeded,
                        }),
                ];
            }
        }

        public MediumMessage Row(Guid id)
        {
            var row = MessagingRows.Medium(id.ToString());
            row.StorageId = id;
            row.StatusName = StatusName.Succeeded;

            return row;
        }
    }
}
