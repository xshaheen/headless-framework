// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

public abstract partial class DataStorageTestsBase
{
    // Wide enough for a statement round trip on the store's clock; the ages under test are hours.
    private static readonly TimeSpan _BacklogClockTolerance = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Moves a stored row's <c>Added</c> back to <paramref name="addedAgo"/> before the store's current time and, when
    /// <paramref name="expiresAgo"/> is given, its <c>ExpiresAt</c> (a delayed row's publish time) back to that far
    /// before it.
    /// </summary>
    protected abstract Task BackdateStoredRowAsync(
        bool published,
        Guid storageId,
        TimeSpan addedAgo,
        TimeSpan? expiresAgo,
        CancellationToken cancellationToken
    );

    public virtual async Task should_count_only_pending_rows_when_reading_the_backlog()
    {
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not expose a monitoring API");
        }

        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();

        // given: nothing stored yet
        var empty = await monitoring.GetBacklogAsync(AbortToken);
        empty.PublishedPending.Should().Be(0);
        empty.ReceivedPending.Should().Be(0);
        empty.OldestPublishedPendingAge.Should().BeNull();
        empty.OldestReceivedPendingAge.Should().BeNull();

        // and: a published row waiting for its first dispatch, one waiting for a persisted retry, and rows that are
        // done (succeeded, failed for good) or not due yet (published for later)
        await _StorePublishedAsync(storage, "backlog-first-dispatch");

        var retrying = await _StorePublishedAsync(storage, "backlog-retrying");
        (
            await storage.ChangePublishStateAsync(
                retrying,
                StatusName.Failed,
                retryDelay: RetryDelay.Exactly(TimeSpan.FromHours(1)),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        var succeeded = await _StorePublishedAsync(storage, "backlog-succeeded");
        (await storage.ChangePublishStateAsync(succeeded, StatusName.Succeeded, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        var failed = await _StorePublishedAsync(storage, "backlog-failed");
        (await storage.ChangePublishStateAsync(failed, StatusName.Failed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        if (Capabilities.SupportsDelayedScheduling)
        {
            await storage.StoreScheduledMessageAsync(
                "backlog-later",
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = CreateMessage(),
                    Content = string.Empty,
                    Lane = MessageLane.Bus,
                },
                TimeProvider.GetUtcNow().AddMinutes(30),
                cancellationToken: AbortToken
            );
        }

        // and: a received row waiting to be handled and one already handled
        await storage.StoreReceivedMessageAsync("backlog-received", "backlog-group", CreateMessage(), AbortToken);
        var handled = await storage.StoreReceivedMessageAsync(
            "backlog-handled",
            "backlog-group",
            CreateMessage(),
            AbortToken
        );
        (await storage.ChangeReceiveStateAsync(handled, StatusName.Succeeded, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        // when
        var backlog = await monitoring.GetBacklogAsync(AbortToken);

        // then: only the rows still waiting count, and each side has an oldest age
        backlog.PublishedPending.Should().Be(2);
        backlog.ReceivedPending.Should().Be(1);
        backlog.OldestPublishedPendingAge.Should().NotBeNull();
        backlog.OldestPublishedPendingAge!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        backlog.OldestPublishedPendingAge.Value.Should().BeLessThan(_BacklogClockTolerance);
        backlog.OldestReceivedPendingAge.Should().NotBeNull();
        backlog.OldestReceivedPendingAge!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        backlog.OldestReceivedPendingAge.Value.Should().BeLessThan(_BacklogClockTolerance);
    }

    public virtual async Task should_age_the_oldest_pending_row_on_the_store_clock_when_reading_the_backlog()
    {
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not expose a monitoring API");
        }

        // given: a published row stored two hours ago and a younger one, and a received row stored 90 minutes ago
        var storage = GetStorage();
        var old = await _StorePublishedAsync(storage, "backlog-old");
        await BackdateStoredRowAsync(published: true, old.StorageId, TimeSpan.FromHours(2), null, AbortToken);
        await _StorePublishedAsync(storage, "backlog-young");

        var received = await storage.StoreReceivedMessageAsync(
            "backlog-received-old",
            "backlog-group",
            CreateMessage(),
            AbortToken
        );
        await BackdateStoredRowAsync(published: false, received.StorageId, TimeSpan.FromMinutes(90), null, AbortToken);

        // when
        var backlog = await storage.GetMonitoringApi().GetBacklogAsync(AbortToken);

        // then
        backlog.PublishedPending.Should().Be(2);
        backlog.OldestPublishedPendingAge.Should().NotBeNull();
        backlog
            .OldestPublishedPendingAge!.Value.Should()
            .BeCloseTo(TimeSpan.FromHours(2), _BacklogClockTolerance)
            .And.BeGreaterThanOrEqualTo(TimeSpan.FromHours(2));
        backlog.ReceivedPending.Should().Be(1);
        backlog
            .OldestReceivedPendingAge!.Value.Should()
            .BeCloseTo(TimeSpan.FromMinutes(90), _BacklogClockTolerance)
            .And.BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(90));
    }

    public virtual async Task should_age_a_delayed_row_from_its_publish_time_when_it_comes_due()
    {
        if (!Capabilities.SupportsMonitoringApi || !Capabilities.SupportsDelayedScheduling)
        {
            Assert.Skip("Storage does not support delayed scheduling and the monitoring API");
        }

        // given: a row stored three hours ago for a publish time that came one hour ago
        var storage = GetStorage();
        var delayed = await storage.StoreScheduledMessageAsync(
            "backlog-delayed",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            TimeProvider.GetUtcNow().AddMinutes(30),
            cancellationToken: AbortToken
        );
        var notDue = await storage.GetMonitoringApi().GetBacklogAsync(AbortToken);
        notDue.PublishedPending.Should().Be(0, "a message published for later does not wait before its time");

        await BackdateStoredRowAsync(
            published: true,
            delayed.StorageId,
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(1),
            AbortToken
        );

        // when
        var backlog = await storage.GetMonitoringApi().GetBacklogAsync(AbortToken);

        // then: it waits from its publish time, not from when it was stored
        backlog.PublishedPending.Should().Be(1);
        backlog
            .OldestPublishedPendingAge!.Value.Should()
            .BeCloseTo(TimeSpan.FromHours(1), _BacklogClockTolerance)
            .And.BeGreaterThanOrEqualTo(TimeSpan.FromHours(1));
    }
}
