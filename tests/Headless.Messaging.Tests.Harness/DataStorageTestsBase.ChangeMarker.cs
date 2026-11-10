// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

public abstract partial class DataStorageTestsBase
{
    /// <summary>
    /// Opens a transaction on the storage's database for a write the test commits later, or returns
    /// <see langword="null"/> for a store with no transactions.
    /// </summary>
    protected virtual Task<DbTransaction?> BeginStorageTransactionAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<DbTransaction?>(null);
    }

    /// <summary>Overwrites a row's change marker in the store, below the storage, to seed a marker ahead of the clock.</summary>
    protected virtual Task SetChangeMarkerAsync(
        bool published,
        Guid storageId,
        DateTimeOffset marker,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException("This storage cannot seed a change marker.");
    }

    public virtual async Task should_move_the_change_marker_forward_on_insert_retry_and_terminal_writes(bool published)
    {
        // given
        var storage = GetStorage();
        var kind = _KindOf(published);
        var start = await _LatestMarkerAsync(storage);

        // when: the row is inserted, fails with a retry pending, then succeeds
        var message = published
            ? await storage.StoreMessageAsync("change-marker-forward", CreateMessage(), cancellationToken: AbortToken)
            : await storage.StoreReceivedMessageAsync(
                "change-marker-forward",
                "change-marker-forward-group",
                CreateMessage(),
                AbortToken
            );
        var inserted = await _ReadChangeAsync(storage, kind, message.StorageId, start);
        inserted.Should().NotBeNull();

        message.Retries = 1;
        (await _ChangeStateAsync(storage, published, message, StatusName.Failed, TimeSpan.FromMinutes(5)))
            .Should()
            .BeTrue();
        var retried = await _ReadChangeAsync(storage, kind, message.StorageId, inserted!.ChangedAt);
        retried.Should().NotBeNull();

        (await _ChangeStateAsync(storage, published, message, StatusName.Succeeded, retryDelay: null))
            .Should()
            .BeTrue();
        var terminal = await _ReadChangeAsync(storage, kind, message.StorageId, retried!.ChangedAt);

        // then: each write moved the marker forward and the read carries the state it wrote
        inserted.Status.Should().Be(StatusName.Scheduled);
        inserted.ChangedAt.Should().BeAfter(start);
        retried.Status.Should().Be(StatusName.Failed);
        retried.ChangedAt.Should().BeAfter(inserted.ChangedAt);
        terminal.Should().NotBeNull();
        terminal!.Status.Should().Be(StatusName.Succeeded);
        terminal.ChangedAt.Should().BeAfter(retried.ChangedAt);
        terminal.Version.Should().BeGreaterThan(retried.Version);
    }

    public virtual async Task should_leave_the_change_marker_on_a_retry_pickup_claim_and_a_lease_renewal(bool published)
    {
        // given: a row due for a retry
        var storage = GetStorage();
        var kind = _KindOf(published);
        var start = await _LatestMarkerAsync(storage);
        var message = await _StoreRetryDueAsync(storage, published, "change-marker-lease");
        var due = await _ReadChangeAsync(storage, kind, message.StorageId, start);
        due.Should().NotBeNull();

        // when: the retry pickup claims it, which leases it without changing its status, and the lease is renewed
        var claimed = (await _PickUpRetriesAsync(storage, published)).Single(m => m.StorageId == message.StorageId);
        (await _RenewAsync(storage, published, claimed, TimeSpan.FromMinutes(1))).Should().BeTrue();

        // then: no change read after the row's marker returns it
        (await _ReadChangeAsync(storage, kind, message.StorageId, due!.ChangedAt))
            .Should()
            .BeNull();

        // and: the state write that ends the attempt moves the marker
        (await _ChangeStateAsync(storage, published, claimed, StatusName.Succeeded, retryDelay: null))
            .Should()
            .BeTrue();
        var terminal = await _ReadChangeAsync(storage, kind, message.StorageId, due.ChangedAt);
        terminal.Should().NotBeNull();
        terminal!.Status.Should().Be(StatusName.Succeeded);
    }

    public virtual async Task should_stamp_a_later_marker_when_the_clock_has_not_passed_the_previous_one(bool published)
    {
        // given: a row whose marker lies an hour past the store's clock, as two writes in one clock tick leave it
        var storage = GetStorage();
        var kind = _KindOf(published);
        var message = published
            ? await storage.StoreMessageAsync("change-marker-tick", CreateMessage(), cancellationToken: AbortToken)
            : await storage.StoreReceivedMessageAsync(
                "change-marker-tick",
                "change-marker-tick-group",
                CreateMessage(),
                AbortToken
            );
        var ahead = DateTimeOffset.UtcNow.AddHours(1);
        await SetChangeMarkerAsync(published, message.StorageId, ahead, AbortToken);

        // when: two state writes run before the clock reaches the marker
        message.Retries = 1;
        (await _ChangeStateAsync(storage, published, message, StatusName.Failed, TimeSpan.FromMinutes(5)))
            .Should()
            .BeTrue();
        var first = await _ReadChangeAsync(storage, kind, message.StorageId, ahead.AddTicks(-1));
        (await _ChangeStateAsync(storage, published, message, StatusName.Succeeded, retryDelay: null))
            .Should()
            .BeTrue();
        var second = await _ReadChangeAsync(storage, kind, message.StorageId, ahead.AddTicks(-1));

        // then: each marker is strictly later than the one before it, by no more than a millisecond
        first.Should().NotBeNull();
        first!.ChangedAt.Should().BeAfter(ahead).And.BeOnOrBefore(ahead.AddMilliseconds(1));
        second.Should().NotBeNull();
        second!.ChangedAt.Should().BeAfter(first.ChangedAt).And.BeOnOrBefore(first.ChangedAt.AddMilliseconds(1));
    }

    public virtual async Task should_read_a_row_again_while_its_marker_lies_inside_the_overlap_window()
    {
        // given: a published and a received row changed after the cursor was taken
        var storage = GetStorage();
        var cursor = await storage.GetMessageChangeStartCursorAsync(AbortToken);
        var published = await storage.StoreMessageAsync(
            "change-overlap",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        var received = await storage.StoreReceivedMessageAsync(
            "change-overlap",
            "change-overlap-group",
            CreateMessage(),
            AbortToken
        );

        // when: the reader drains the changes, then reads once more with nothing new written
        var overlap = TimeSpan.FromSeconds(30);
        var (firstRead, drained) = await _DrainChangesAsync(storage, cursor, overlap);
        var (again, last) = await _DrainChangesAsync(storage, drained, overlap);

        // then: both rows come back, and come back again with the same version while inside the window
        firstRead.Should().Contain(c => c.Kind == MessageChangeKind.Published && c.Id == published.StorageId);
        firstRead.Should().Contain(c => c.Kind == MessageChangeKind.Received && c.Id == received.StorageId);
        var version = firstRead.First(c => c.Id == published.StorageId).Version;
        again.Should().Contain(c => c.Id == published.StorageId);
        again
            .Where(c => c.Id == published.StorageId)
            .Should()
            .OnlyContain(c => c.Version == version, "a repeat inside the window is a duplicate");
        again.Should().Contain(c => c.Id == received.StorageId);

        // and: the cursor survives its text form
        MessageChangeCursor.TryParse(last.ToString(), out var parsed).Should().BeTrue();
        parsed.Should().Be(last);
    }

    public virtual async Task should_return_a_row_committed_late_inside_the_overlap_window()
    {
        // given: a row stamped in a transaction that is still open while a later row commits, and two readers, one
        // with an overlap window and one without
        var storage = GetStorage();
        var start = await storage.GetMessageChangeStartCursorAsync(AbortToken);
        await using var transaction = await BeginStorageTransactionAsync(AbortToken);
        transaction.Should().NotBeNull("the provider opts in to this test only when it has transactions");
        await using var connection = transaction!.Connection!;
        var late = await storage.StoreMessageAsync("change-late", CreateMessage(), transaction, AbortToken);
        var early = await storage.StoreMessageAsync("change-early", CreateMessage(), cancellationToken: AbortToken);

        // when: both readers pass the committed row before the late one commits, then read on after its commit
        var overlap = TimeSpan.FromSeconds(30);
        var (beforeCommit, overlapping) = await _DrainChangesAsync(storage, start, overlap);
        var (_, forwardOnly) = await _DrainChangesAsync(storage, start, TimeSpan.Zero);
        await transaction.CommitAsync(AbortToken);
        var (afterCommit, _) = await _DrainChangesAsync(storage, overlapping, overlap);
        var (withoutOverlap, _) = await _DrainChangesAsync(storage, forwardOnly, TimeSpan.Zero);

        // then: the late row was invisible, then returned by the pass that re-reads the overlap window
        beforeCommit.Should().Contain(c => c.Id == early.StorageId);
        beforeCommit.Should().NotContain(c => c.Id == late.StorageId);
        // A drain repeats a row while it lies inside the window, so the late row may come back more than once, always
        // with the version it was stamped with.
        var lateChanges = afterCommit.Where(c => c.Id == late.StorageId).ToList();
        lateChanges.Should().NotBeEmpty().And.OnlyContain(c => c.Version == lateChanges[0].Version);
        lateChanges[0]
            .ChangedAt.Should()
            .BeOnOrBefore(forwardOnly.PassThrough, "the late row was stamped before the readers' passes ended");

        // and: a reader without the overlap window advanced past it and lost it
        withoutOverlap.Should().NotContain(c => c.Id == late.StorageId);
    }

    private static MessageChangeKind _KindOf(bool published) =>
        published ? MessageChangeKind.Published : MessageChangeKind.Received;

    private static async Task<DateTimeOffset> _LatestMarkerAsync(IDataStorage storage) =>
        await storage.GetLatestMessageChangeMarkerAsync(AbortToken) ?? DateTimeOffset.MinValue;

    /// <summary>Returns the change of one row with a marker after <paramref name="after"/>, or <see langword="null"/>.</summary>
    private static async Task<MessageChange?> _ReadChangeAsync(
        IDataStorage storage,
        MessageChangeKind kind,
        Guid id,
        DateTimeOffset after
    )
    {
        var through = await _LatestMarkerAsync(storage);
        MessageChangePosition? position = null;

        while (true)
        {
            var page = await storage.ReadChangedMessagesAsync(kind, after, through, position, 500, AbortToken);

            if (page.FirstOrDefault(change => change.Id == id) is { } found)
            {
                return found;
            }

            if (page.Count < 500)
            {
                return null;
            }

            position = new MessageChangePosition(page[^1].ChangedAt, page[^1].Id);
        }
    }

    /// <summary>Reads changes until a read returns a cursor whose pass has nothing more to page through.</summary>
    private static async Task<(List<MessageChange> Changes, MessageChangeCursor Cursor)> _DrainChangesAsync(
        IDataStorage storage,
        MessageChangeCursor cursor,
        TimeSpan overlapWindow
    )
    {
        var changes = new List<MessageChange>();

        // Two consecutive reads that return a page smaller than the page size have finished the current pass and the
        // one after it, so every row committed before the first of them has been seen.
        var shortReads = 0;
        for (var read = 0; read < 1000 && shortReads < 2; read++)
        {
            var page = await storage.GetMessageChangesAsync(cursor, overlapWindow, 500, AbortToken);
            changes.AddRange(page.Changes);
            cursor = page.Cursor;
            shortReads = page.Changes.Count < 500 ? shortReads + 1 : 0;
        }

        return (changes, cursor);
    }

    private static async Task<bool> _ChangeStateAsync(
        IDataStorage storage,
        bool published,
        MediumMessage message,
        StatusName state,
        TimeSpan? retryDelay
    )
    {
        var delay = retryDelay is { } value ? RetryDelay.Exactly(value) : (RetryDelay?)null;

        return published
            ? await storage.ChangePublishStateAsync(message, state, retryDelay: delay, cancellationToken: AbortToken)
            : await storage.ChangeReceiveStateAsync(message, state, retryDelay: delay, cancellationToken: AbortToken);
    }
}
