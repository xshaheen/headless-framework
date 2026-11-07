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
        var mark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value;
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
