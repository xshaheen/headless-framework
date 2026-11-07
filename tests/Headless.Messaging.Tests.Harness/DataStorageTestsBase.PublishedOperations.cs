// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

// Audited re-send and delete of published rows, single-row and as Operations over a frozen scope. Every case uses a
// message name and an operator of its own, so rows and receipts other cases leave in a shared store never reach its
// assertions.
public abstract partial class DataStorageTestsBase
{
    public virtual async Task should_record_a_published_receipt_with_a_null_reason_for_a_single_resend()
    {
        // given: two published rows that failed for good
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var actor = $"resend-{Guid.NewGuid():N}";
        var name = $"resend-single-{Guid.NewGuid():N}";
        var first = await _FailPublishedAsync(storage, name);
        var second = await _FailPublishedAsync(storage, name);

        // when: an operator re-sends each without a reason
        var firstResult = await published.ResendAsync(
            new PublishedOperationRequest(Guid.NewGuid(), first.StorageId, null, Reason: null, _Operator(actor)),
            AbortToken
        );
        var secondResult = await published.ResendAsync(
            new PublishedOperationRequest(Guid.NewGuid(), second.StorageId, null, Reason: "  ", _Operator(actor)),
            AbortToken
        );
        var ledger = await storage
            .GetInboxOperationsApi()
            .QueryReceiptsAsync(
                new MessagingOperationReceiptQuery
                {
                    Actor = actor,
                    TargetKind = MessagingOperationTargetKind.Published,
                },
                _Viewer(),
                AbortToken
            );
        var inboxOnly = await storage
            .GetInboxOperationsApi()
            .QueryReceiptsAsync(
                new MessagingOperationReceiptQuery { Actor = actor, TargetKind = MessagingOperationTargetKind.Inbox },
                _Viewer(),
                AbortToken
            );

        // then: both rows wait for their retry pickup with a fresh budget, and the ledger holds a published receipt of
        // each, newest first, with no reason
        firstResult.Outcome.Should().Be(InboxOperationOutcome.Applied);
        firstResult.Reason.Should().BeNull();
        secondResult.Outcome.Should().Be(InboxOperationOutcome.Applied);
        var row = await storage.GetMonitoringApi().GetPublishedMessageAsync(first.StorageId, AbortToken);
        row!.StatusName.Should().Be(StatusName.Failed);
        row.NextRetryAt.Should().NotBeNull();
        row.Retries.Should().Be(0);
        ledger
            .Items.Select(static receipt => receipt.OperationId)
            .Should()
            .BeEquivalentTo([firstResult.OperationId, secondResult.OperationId]);
        ledger.Items.Should().BeInDescendingOrder(static receipt => receipt.CreatedAt);
        ledger.Items.Should().OnlyContain(static receipt => receipt.Reason == null);
        ledger.Items.Should().OnlyContain(static receipt => receipt.OperationType == MessagingOperationType.Resend);
        ledger.Items.Should().OnlyContain(static receipt => receipt.Outcome == InboxOperationOutcome.Applied);
        ledger.Items.Should().OnlyContain(receipt => receipt.MessageName == name);
        var firstReceipt = ledger
            .Items.Should()
            .ContainSingle(receipt => receipt.OperationId == firstResult.OperationId)
            .Which;
        firstReceipt.StorageId.Should().Be(first.StorageId);
        firstReceipt.IncarnationId.Should().BeNull();
        inboxOnly.Items.Should().BeEmpty();
    }

    public virtual async Task should_delete_a_published_row_with_a_receipt_and_record_a_refused_delete_of_a_missing_row()
    {
        // given: a published row that succeeded
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var actor = $"purge-{Guid.NewGuid():N}";
        var message = await storage.StoreMessageAsync(
            $"purge-{Guid.NewGuid():N}",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        (
            await storage.ChangePublishStateAsync(
                message,
                StatusName.Succeeded,
                retryDelay: null,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        // when: one operator deletes it, then another delete of it arrives under a new operation id
        var deleted = await published.PurgeAsync(
            new PublishedOperationRequest(Guid.NewGuid(), message.StorageId, null, "clean up", _Operator(actor)),
            AbortToken
        );
        var refused = await published.PurgeAsync(
            new PublishedOperationRequest(Guid.NewGuid(), message.StorageId, null, "clean up", _Operator(actor)),
            AbortToken
        );
        var ledger = await storage
            .GetInboxOperationsApi()
            .QueryReceiptsAsync(new MessagingOperationReceiptQuery { Actor = actor }, _Viewer(), AbortToken);

        // then: the row is gone, and both deletes are in the ledger as published receipts, the refused one included
        deleted.Outcome.Should().Be(InboxOperationOutcome.Applied);
        refused.Outcome.Should().Be(InboxOperationOutcome.NotFound);
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken)).Should().BeNull();
        ledger.Items.Should().HaveCount(2);
        ledger
            .Items.Should()
            .OnlyContain(static receipt => receipt.TargetKind == MessagingOperationTargetKind.Published);
        ledger.Items.Should().OnlyContain(static receipt => receipt.OperationType == MessagingOperationType.Purge);
        ledger.Items.Should().OnlyContain(receipt => receipt.StorageId == message.StorageId);
        ledger
            .Items.Should()
            .ContainSingle(receipt => receipt.OperationId == deleted.OperationId)
            .Which.Reason.Should()
            .Be("clean up");
        ledger
            .Items.Should()
            .ContainSingle(receipt => receipt.OperationId == refused.OperationId)
            .Which.Outcome.Should()
            .Be(InboxOperationOutcome.NotFound);
    }

    public virtual async Task should_leave_a_failure_written_after_confirm_out_of_a_published_scope()
    {
        // given: two failures for good and one still waiting for a retry, then the mark, then a late failure
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var name = $"published-scope-{Guid.NewGuid():N}";
        var first = await _FailPublishedAsync(storage, name);
        var second = await _FailPublishedAsync(storage, name);
        await _FailPublishedAsync(storage, name, RetryDelay.Exactly(TimeSpan.FromHours(1)));
        var mark = await _SeedMarkerBeforeNowAsync(published: true, first.StorageId, second.StorageId);
        var late = await _FailPublishedAsync(storage, name);
        var scope = new MessageOperationScope
        {
            Kind = MessageOperationKind.Resend,
            Query = new MessageListQuery { MessageType = MessageType.Publish, Name = name },
            HighWaterMark = mark,
        };

        // when
        var count = await published.CountOperationScopeAsync(scope, cancellationToken: AbortToken);
        var targets = await _ReadWholePublishedScopeAsync(published, scope, chunkSize: 1);
        var next = await _ReadWholePublishedScopeAsync(
            published,
            scope with
            {
                HighWaterMark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value,
            },
            chunkSize: 10
        );

        // then: the frozen scope holds the two failures from before the mark; a later confirm sees the late one too
        count.Should().Be(new MessageCount(2, Capped: false));
        targets.Select(static target => target.StorageId).Should().BeEquivalentTo([first.StorageId, second.StorageId]);
        targets.Should().OnlyContain(static target => target.Status == StatusName.Failed);
        targets.Should().OnlyContain(target => target.ChangedAt <= mark);
        next.Select(static target => target.StorageId)
            .Should()
            .BeEquivalentTo([first.StorageId, second.StorageId, late.StorageId]);
    }

    public virtual async Task should_refuse_a_published_chunk_write_over_a_row_re_sent_and_failed_again_after_the_read()
    {
        // given: two failures for good in a confirmed re-send scope, and a chunk one node read
        var storage = GetStorage();
        var nodeA = storage.GetPublishedOperationsApi();
        var nodeB = storage.GetPublishedOperationsApi();
        var actor = $"published-race-{Guid.NewGuid():N}";
        var name = $"published-race-{Guid.NewGuid():N}";
        var raced = await _FailPublishedAsync(storage, name);
        var other = await _FailPublishedAsync(storage, name);
        var scope = new MessageOperationScope
        {
            Kind = MessageOperationKind.Resend,
            Query = new MessageListQuery { MessageType = MessageType.Publish, Name = name },
            HighWaterMark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value,
        };
        var staleChunk = await _ReadWholePublishedScopeAsync(nodeB, scope, chunkSize: 10);
        staleChunk.Should().HaveCount(2);

        // when: another node re-sends one row, the send fails again for good, and then the first node writes its chunk
        var racedTarget = staleChunk.Single(target => target.StorageId == raced.StorageId);
        (await nodeA.ResendAsync(_PublishedRequest(racedTarget, actor), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        (
            await storage.ChangePublishStateAsync(
                raced,
                StatusName.Failed,
                retryDelay: null,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        var staleResults = new List<PublishedOperationResult>();
        foreach (var target in staleChunk)
        {
            staleResults.Add(await nodeB.ResendAsync(_PublishedRequest(target, actor), AbortToken));
        }

        var rerun = await _ReadWholePublishedScopeAsync(nodeA, scope, chunkSize: 10);
        var ledger = await storage
            .GetInboxOperationsApi()
            .QueryReceiptsAsync(
                new MessagingOperationReceiptQuery
                {
                    Actor = actor,
                    TargetKind = MessagingOperationTargetKind.Published,
                },
                _Viewer(),
                AbortToken
            );

        // then: the stale write over the raced row is a refusal, the other row is re-sent, running the Operation again
        // finds nothing, and no row was re-sent twice
        staleResults
            .Single(result => result.StorageId == raced.StorageId)
            .Outcome.Should()
            .Be(InboxOperationOutcome.StateConflict);
        staleResults
            .Single(result => result.StorageId == other.StorageId)
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        var racedRow = await storage.GetMonitoringApi().GetPublishedMessageAsync(raced.StorageId, AbortToken);
        racedRow!.NextRetryAt.Should().BeNull("the refused write left the row that failed again as it was");
        rerun.Should().BeEmpty();
        ledger
            .Items.Where(static receipt => receipt.Outcome == InboxOperationOutcome.Applied)
            .Select(static receipt => receipt.StorageId)
            .Should()
            .BeEquivalentTo([raced.StorageId, other.StorageId]);
        ledger.Items.Should().HaveCount(3);
    }

    public virtual async Task should_refuse_a_published_write_without_a_named_operator()
    {
        // given: a published row that failed for good, and a viewer authenticated without an operator name
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var name = $"published-viewer-{Guid.NewGuid():N}";
        var message = await _FailPublishedAsync(storage, name);
        var viewer = _Viewer();
        var scope = new MessageOperationScope
        {
            Kind = MessageOperationKind.Delete,
            Query = new MessageListQuery { MessageType = MessageType.Publish, Name = name },
            HighWaterMark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value,
        };

        // when
        var count = await published.CountOperationScopeAsync(scope, cancellationToken: AbortToken);
        var resend = async () =>
            await published.ResendAsync(
                new PublishedOperationRequest(Guid.NewGuid(), message.StorageId, null, "viewer re-sends", viewer),
                AbortToken
            );
        var purge = async () =>
            await published.PurgeAsync(
                new PublishedOperationRequest(Guid.NewGuid(), message.StorageId, null, "viewer deletes", viewer),
                AbortToken
            );

        // then: the scope reads, both writes are refused, and the row is unchanged
        count.Should().Be(new MessageCount(1, Capped: false));
        await resend.Should().ThrowAsync<UnauthorizedAccessException>();
        await purge.Should().ThrowAsync<UnauthorizedAccessException>();
        var row = await storage.GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken);
        row.Should().NotBeNull();
        row!.NextRetryAt.Should().BeNull();
    }

    /// <summary>
    /// Gives a published row a send lease that stays live for <paramref name="liveFor"/> on the store's clock, written
    /// below the storage: the storage takes no lease on a terminal row, yet a lease a sender took before the row turned
    /// terminal can still be live when an operator acts on it.
    /// </summary>
    protected virtual Task SetPublishedLeaseAsync(Guid storageId, TimeSpan liveFor, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This storage cannot seed a published send lease.");
    }

    public virtual async Task should_leave_a_published_row_with_a_pending_retry_untouched()
    {
        // given: a published row that failed with a retry still pending
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var actor = $"published-pending-{Guid.NewGuid():N}";
        var message = await _FailPublishedAsync(
            storage,
            $"published-pending-{Guid.NewGuid():N}",
            RetryDelay.Exactly(TimeSpan.FromHours(1))
        );
        var before = await _PublishedSnapshotAsync(storage, message.StorageId);

        // when: an operator re-sends it, then deletes it
        var resend = await published.ResendAsync(
            new PublishedOperationRequest(Guid.NewGuid(), message.StorageId, null, "re-send", _Operator(actor)),
            AbortToken
        );
        var purge = await published.PurgeAsync(
            new PublishedOperationRequest(Guid.NewGuid(), message.StorageId, null, "delete", _Operator(actor)),
            AbortToken
        );

        // then: the pipeline still owns the row, so both are refused as active and the row is as it was
        resend.Outcome.Should().Be(InboxOperationOutcome.Active);
        purge.Outcome.Should().Be(InboxOperationOutcome.Active);
        (await _PublishedSnapshotAsync(storage, message.StorageId)).Should().Be(before);
    }

    public virtual async Task should_leave_a_published_row_with_a_live_send_lease_untouched()
    {
        // given: a published row that failed for good while a sender still holds its lease
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var actor = $"published-leased-{Guid.NewGuid():N}";
        var message = await _FailPublishedAsync(storage, $"published-leased-{Guid.NewGuid():N}");
        await SetPublishedLeaseAsync(message.StorageId, TimeSpan.FromHours(1), AbortToken);
        var before = await _PublishedSnapshotAsync(storage, message.StorageId);

        // when: an operator re-sends it, then deletes it
        var resend = await published.ResendAsync(
            new PublishedOperationRequest(Guid.NewGuid(), message.StorageId, null, "re-send", _Operator(actor)),
            AbortToken
        );
        var purge = await published.PurgeAsync(
            new PublishedOperationRequest(Guid.NewGuid(), message.StorageId, null, "delete", _Operator(actor)),
            AbortToken
        );

        // then: a send is in flight, so both are refused as active and the row is as it was
        resend.Outcome.Should().Be(InboxOperationOutcome.Active);
        purge.Outcome.Should().Be(InboxOperationOutcome.Active);
        (await _PublishedSnapshotAsync(storage, message.StorageId)).Should().Be(before);
    }

    public virtual async Task should_refuse_to_resend_a_published_row_that_succeeded()
    {
        // given: a published row that succeeded
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var actor = $"published-succeeded-{Guid.NewGuid():N}";
        var message = await _SucceedPublishedAsync(storage, $"published-succeeded-{Guid.NewGuid():N}");
        var before = await _PublishedSnapshotAsync(storage, message.StorageId);

        // when
        var result = await published.ResendAsync(
            new PublishedOperationRequest(Guid.NewGuid(), message.StorageId, null, "re-send", _Operator(actor)),
            AbortToken
        );
        var ledger = await _PublishedLedgerAsync(storage, actor);

        // then: only a failure is re-sent; the refusal is in the ledger and the row is as it was
        result.Outcome.Should().Be(InboxOperationOutcome.StateConflict);
        (await _PublishedSnapshotAsync(storage, message.StorageId)).Should().Be(before);
        ledger.Items.Should().ContainSingle().Which.Outcome.Should().Be(InboxOperationOutcome.StateConflict);
    }

    public virtual async Task should_replay_a_repeated_published_request_with_one_receipt()
    {
        // given: a published row that failed for good
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var actor = $"published-replay-{Guid.NewGuid():N}";
        var message = await _FailPublishedAsync(storage, $"published-replay-{Guid.NewGuid():N}");
        var request = new PublishedOperationRequest(
            Guid.NewGuid(),
            message.StorageId,
            null,
            "re-send after repair",
            _Operator(actor)
        );

        // when: the same request arrives twice
        var first = await published.ResendAsync(request, AbortToken);
        var replay = await published.ResendAsync(request, AbortToken);
        var ledger = await _PublishedLedgerAsync(storage, actor);

        // then: the second returns the first's receipt, flagged as a replay, and the ledger holds one receipt
        first.Outcome.Should().Be(InboxOperationOutcome.Applied);
        first.IsReplay.Should().BeFalse();
        replay.Should().Be(first with { IsReplay = true });
        ledger.Items.Should().ContainSingle().Which.OperationId.Should().Be(request.OperationId);
    }

    public virtual async Task should_refuse_a_published_operation_id_reused_by_a_different_request()
    {
        // given: one failed row re-sent under an operation id, and another failed row
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var actor = $"published-conflict-{Guid.NewGuid():N}";
        var name = $"published-conflict-{Guid.NewGuid():N}";
        var message = await _FailPublishedAsync(storage, name);
        var other = await _FailPublishedAsync(storage, name);
        var request = new PublishedOperationRequest(
            Guid.NewGuid(),
            message.StorageId,
            null,
            "re-send after repair",
            _Operator(actor)
        );
        var first = await published.ResendAsync(request, AbortToken);
        var otherBefore = await _PublishedSnapshotAsync(storage, other.StorageId);

        // when: the same operation id arrives for another row, with another reason, and as a delete
        var otherRow = await published.ResendAsync(request with { StorageId = other.StorageId }, AbortToken);
        var otherReason = await published.ResendAsync(request with { Reason = "another reason" }, AbortToken);
        var otherType = await published.PurgeAsync(request, AbortToken);
        var ledger = await _PublishedLedgerAsync(storage, actor);

        // then: each is a refused conflict that changes nothing, and the ledger keeps the first receipt alone
        first.Outcome.Should().Be(InboxOperationOutcome.Applied);
        foreach (var conflict in new[] { otherRow, otherReason, otherType })
        {
            conflict.Outcome.Should().Be(InboxOperationOutcome.OperationConflict);
            conflict.IsReplay.Should().BeTrue();
            conflict.OperationId.Should().Be(request.OperationId);
        }

        otherRow.StorageId.Should().Be(other.StorageId);
        otherReason.Reason.Should().Be("another reason");
        otherType.OperationType.Should().Be(MessagingOperationType.Purge);
        (await _PublishedSnapshotAsync(storage, other.StorageId)).Should().Be(otherBefore);
        (await storage.GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken))
            .Should()
            .NotBeNull("the delete under a used id was refused");
        var receipt = ledger.Items.Should().ContainSingle().Which;
        receipt.OperationId.Should().Be(request.OperationId);
        receipt.StorageId.Should().Be(message.StorageId);
        receipt.Reason.Should().Be("re-send after repair");
        receipt.OperationType.Should().Be(MessagingOperationType.Resend);
        receipt.Outcome.Should().Be(InboxOperationOutcome.Applied);
    }

    public virtual async Task should_scope_a_published_delete_to_both_terminal_statuses_and_a_resend_to_failures()
    {
        // given: a success, a failure for good, and a failure still waiting for a retry
        var storage = GetStorage();
        var published = storage.GetPublishedOperationsApi();
        var name = $"published-delete-scope-{Guid.NewGuid():N}";
        var succeeded = await _SucceedPublishedAsync(storage, name);
        var failed = await _FailPublishedAsync(storage, name);
        await _FailPublishedAsync(storage, name, RetryDelay.Exactly(TimeSpan.FromHours(1)));
        var delete = new MessageOperationScope
        {
            Kind = MessageOperationKind.Delete,
            Query = new MessageListQuery { MessageType = MessageType.Publish, Name = name },
            HighWaterMark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value,
        };
        var resend = delete with { Kind = MessageOperationKind.Resend };

        // when
        var deleteCount = await published.CountOperationScopeAsync(delete, cancellationToken: AbortToken);
        var deleteTargets = await _ReadWholePublishedScopeAsync(published, delete, chunkSize: 1);
        var resendCount = await published.CountOperationScopeAsync(resend, cancellationToken: AbortToken);
        var resendTargets = await _ReadWholePublishedScopeAsync(published, resend, chunkSize: 1);

        // then: a delete takes both terminal rows, a re-send the failure only, and neither the pending retry
        deleteCount.Should().Be(new MessageCount(2, Capped: false));
        deleteTargets
            .Select(static target => target.StorageId)
            .Should()
            .BeEquivalentTo([succeeded.StorageId, failed.StorageId]);
        deleteTargets
            .Single(target => target.StorageId == succeeded.StorageId)
            .Status.Should()
            .Be(StatusName.Succeeded);
        resendCount.Should().Be(new MessageCount(1, Capped: false));
        resendTargets.Select(static target => target.StorageId).Should().Equal(failed.StorageId);
        resendTargets.Should().OnlyContain(static target => target.Status == StatusName.Failed);
    }

    private static async Task<MediumMessage> _SucceedPublishedAsync(IDataStorage storage, string name)
    {
        var message = await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken);
        (
            await storage.ChangePublishStateAsync(
                message,
                StatusName.Succeeded,
                retryDelay: null,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        return message;
    }

    /// <summary>The fields of a published row an operator write would change.</summary>
    private static async Task<PublishedRowSnapshot> _PublishedSnapshotAsync(IDataStorage storage, Guid storageId)
    {
        var row = await storage.GetMonitoringApi().GetPublishedMessageAsync(storageId, AbortToken);
        row.Should().NotBeNull();

        return new PublishedRowSnapshot(
            row!.StatusName,
            row.NextRetryAt,
            row.Retries,
            row.InlineAttempts,
            row.ExpiresAt,
            row.LockedUntil,
            row.Owner
        );
    }

    private static ValueTask<MessagingOperationReceiptPage> _PublishedLedgerAsync(IDataStorage storage, string actor) =>
        storage
            .GetInboxOperationsApi()
            .QueryReceiptsAsync(
                new MessagingOperationReceiptQuery
                {
                    Actor = actor,
                    TargetKind = MessagingOperationTargetKind.Published,
                },
                _Viewer(),
                AbortToken
            );

    private sealed record PublishedRowSnapshot(
        StatusName? Status,
        DateTimeOffset? NextRetryAt,
        int Retries,
        int InlineAttempts,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? LockedUntil,
        string? Owner
    );

    /// <summary>
    /// Moves the given rows' change markers an hour into the past and returns that instant as a confirmed mark.
    /// </summary>
    /// <remarks>
    /// Every write after it stamps the store's clock, which is then strictly later than the mark. Confirming at the
    /// store's latest marker instead races the clock: a row written in the same clock tick, or after a row stamped
    /// one tick ahead of the clock, carries a marker at or before that mark and lands inside the frozen scope.
    /// Whole milliseconds keep the value exact in every store's timestamp precision.
    /// </remarks>
    private async Task<DateTimeOffset> _SeedMarkerBeforeNowAsync(bool published, params Guid[] storageIds)
    {
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        var mark = new DateTimeOffset(past.Ticks - (past.Ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
        foreach (var storageId in storageIds)
        {
            await SetChangeMarkerAsync(published, storageId, mark, AbortToken);
        }

        return mark;
    }

    private static async Task<MediumMessage> _FailPublishedAsync(
        IDataStorage storage,
        string name,
        RetryDelay? retryDelay = null
    )
    {
        var message = await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken);
        (
            await storage.ChangePublishStateAsync(
                message,
                StatusName.Failed,
                retryDelay: retryDelay,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        return message;
    }

    private static async Task<List<MessageOperationTarget>> _ReadWholePublishedScopeAsync(
        IPublishedOperationsApi operations,
        MessageOperationScope scope,
        int chunkSize
    )
    {
        var targets = new List<MessageOperationTarget>();
        Guid? after = null;
        for (var chunks = 0; chunks < 50; chunks++)
        {
            var chunk = await operations.ReadOperationScopeAsync(scope, after, chunkSize, AbortToken);
            targets.AddRange(chunk.Targets);
            if (chunk.Next is not { } next)
            {
                break;
            }

            after = next;
        }

        return targets;
    }

    private static PublishedOperationRequest _PublishedRequest(MessageOperationTarget target, string actor) =>
        new(Guid.NewGuid(), target.StorageId, target.ChangedAt, "bulk re-send", _Operator(actor));
}
