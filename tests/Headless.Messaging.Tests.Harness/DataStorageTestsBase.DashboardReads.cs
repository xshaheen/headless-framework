// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

// The reads and chunked writes the dashboard module runs against storage. Every case filters by a name, consumer, actor,
// or fingerprint of its own, so rows other cases leave in a shared store never reach its assertions.
public abstract partial class DataStorageTestsBase
{
    public virtual async Task should_page_a_keyset_list_without_gaps_or_repeats_while_rows_change_between_pages()
    {
        // given: 250 published rows of one name
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var name = $"keyset-list-{Guid.NewGuid():N}";
        var stored = new List<MediumMessage>();
        for (var i = 0; i < 250; i++)
        {
            stored.Add(await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken));
        }

        var query = new MessageListQuery { MessageType = MessageType.Publish, Name = name };
        var seen = new List<Guid>();
        MessageListPosition? after = null;
        var pages = 0;

        // when: each page is read after rows not yet read change state, which moves their change marker
        do
        {
            var page = await monitoring.ListMessagesAsync(query, after, pageSize: 40, AbortToken);
            seen.AddRange(page.Items.Select(static item => item.StorageId));
            after = page.Next;
            pages++;

            var unread = stored.Where(message => !seen.Contains(message.StorageId)).Take(5).ToList();
            foreach (var message in unread)
            {
                (
                    await storage.ChangePublishStateAsync(
                        message,
                        StatusName.Failed,
                        retryDelay: RetryDelay.Exactly(TimeSpan.FromMinutes(5)),
                        cancellationToken: AbortToken
                    )
                )
                    .Should()
                    .BeTrue();
            }
        } while (after is not null && pages < 20);

        // then: every original row was read exactly once
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().BeEquivalentTo(stored.Select(static message => message.StorageId));
        pages.Should().Be(7);
    }

    public virtual async Task should_return_the_cap_and_capped_when_a_count_exceeds_the_cap()
    {
        // given: five scheduled published rows of one name
        var storage = GetStorage();
        var monitoring = storage.GetMonitoringApi();
        var name = $"capped-count-{Guid.NewGuid():N}";
        for (var i = 0; i < 5; i++)
        {
            await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken);
        }

        var query = new MessageListQuery
        {
            MessageType = MessageType.Publish,
            Name = name,
            Status = StatusName.Scheduled,
        };

        // when
        var over = await monitoring.CountMessagesAsync(query, cap: 3, AbortToken);
        var at = await monitoring.CountMessagesAsync(query, cap: 5, AbortToken);
        var under = await monitoring.CountMessagesAsync(query, cap: 10, AbortToken);
        var terminalFailed = await monitoring.CountMessagesAsync(
            query with
            {
                Status = StatusName.Failed,
                TerminalOnly = true,
            },
            cap: 10,
            AbortToken
        );

        // then
        over.Should().Be(new MessageCount(3, Capped: true));
        at.Should().Be(new MessageCount(5, Capped: false));
        under.Should().Be(new MessageCount(5, Capped: false));
        terminalFailed.Should().Be(new MessageCount(0, Capped: false));
    }

    public virtual async Task should_group_terminal_failures_by_fingerprint_and_consumer_bytes()
    {
        // given: two same-cause failures of one consumer, one of a second consumer, and one of a consumer whose
        // identity differs from the first only by a trailing space
        var storage = GetStorage();
        var fingerprint = $"v1:{Guid.NewGuid():N}";
        var consumer = $"groups-consumer-{Guid.NewGuid():N}";
        var other = consumer + "-other";
        var padded = consumer + " ";
        await _FailReceivedAsync(storage, consumer, fingerprint);
        await _FailReceivedAsync(storage, consumer, fingerprint);
        await _FailReceivedAsync(storage, other, fingerprint);
        await _FailReceivedAsync(storage, padded, fingerprint);

        // when
        var groups = (await storage.GetMonitoringApi().GetFailureGroupsAsync(MessageType.Subscribe, null, AbortToken))
            .Where(group => string.Equals(group.FailureFingerprint, fingerprint, StringComparison.Ordinal))
            .ToList();
        var oneConsumer = await storage
            .GetMonitoringApi()
            .GetFailureGroupsAsync(MessageType.Subscribe, consumer, AbortToken);

        // then
        groups.Should().HaveCount(3);
        groups.Should().ContainSingle(group => group.ConsumerIdentity == consumer).Which.Count.Should().Be(2);
        groups.Should().ContainSingle(group => group.ConsumerIdentity == other).Which.Count.Should().Be(1);
        groups.Should().ContainSingle(group => group.ConsumerIdentity == padded).Which.Count.Should().Be(1);
        groups.Should().OnlyContain(group => group.MessageType == MessageType.Subscribe);
        oneConsumer.Should().ContainSingle();
        oneConsumer[0].ConsumerIdentity.Should().Be(consumer);
        oneConsumer[0].Count.Should().Be(2);
    }

    public virtual async Task should_group_published_failures_by_fingerprint_without_a_consumer()
    {
        // given: two published failures with one cause and one still waiting for a retry
        var storage = GetStorage();
        var fingerprint = $"v1:{Guid.NewGuid():N}";
        var name = $"groups-published-{Guid.NewGuid():N}";
        for (var i = 0; i < 3; i++)
        {
            var message = await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken);
            message.FailureFingerprint = fingerprint;
            message.ExceptionInfo = "broker refused";
            (
                await storage.ChangePublishStateAsync(
                    message,
                    StatusName.Failed,
                    retryDelay: i == 2 ? RetryDelay.Exactly(TimeSpan.FromMinutes(5)) : null,
                    cancellationToken: AbortToken
                )
            )
                .Should()
                .BeTrue();
        }

        // when
        var groups = (await storage.GetMonitoringApi().GetFailureGroupsAsync(MessageType.Publish, null, AbortToken))
            .Where(group => string.Equals(group.FailureFingerprint, fingerprint, StringComparison.Ordinal))
            .ToList();

        // then: the failure with a retry pending has not ended, so it is not grouped
        groups.Should().ContainSingle();
        groups[0].Count.Should().Be(2);
        groups[0].ConsumerIdentity.Should().BeNull();
        groups[0].MessageType.Should().Be(MessageType.Publish);
    }

    public virtual async Task should_freeze_an_operation_scope_at_its_high_water_mark_and_never_re_execute_a_message_twice()
    {
        // given: two terminal failures of one consumer, then the mark the operator confirmed at, then a third failure
        var storage = GetStorage();
        var operations = storage.GetInboxOperationsApi();
        var consumer = $"scope-consumer-{Guid.NewGuid():N}";
        var first = await _AdmitTerminalInboxAsync(storage, consumer);
        var second = await _AdmitTerminalInboxAsync(storage, consumer);
        var mark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value;
        var late = await _AdmitTerminalInboxAsync(storage, consumer);
        var scope = new MessageOperationScope
        {
            Kind = MessageOperationKind.ReExecute,
            Query = new MessageListQuery { MessageType = MessageType.Subscribe, ConsumerIdentity = consumer },
            HighWaterMark = mark,
        };

        // when: the Operation runs in chunks of one
        var count = await operations.CountOperationScopeAsync(scope, cancellationToken: AbortToken);
        var targets = await _ReadWholeScopeAsync(operations, scope, chunkSize: 1);
        var applied = new List<InboxOperationResult>();
        foreach (var target in targets)
        {
            applied.Add(await operations.ForceReprocessAsync(_OperatorRequest(target, "operator-a"), AbortToken));
        }

        // then: the scope held the two failures from before the mark, and both were re-executed
        count.Should().Be(new MessageCount(2, Capped: false));
        targets
            .Select(static target => target.IncarnationId)
            .Should()
            .BeEquivalentTo([first.InboxGeneration!.IncarnationId, second.InboxGeneration!.IncarnationId]);
        targets.Should().NotContain(target => target.IncarnationId == late.InboxGeneration!.IncarnationId);
        applied.Should().OnlyContain(static result => result.Outcome == InboxOperationOutcome.Applied);

        // when: the children fail again, and the same Operation and a newly confirmed one read their scopes
        var parents = new[] { first, second }.ToDictionary(static message => message.InboxGeneration!.IncarnationId);
        foreach (var result in applied)
        {
            await _FailChildAsync(storage, result, parents[result.ExpectedIncarnationId]);
        }

        var rerun = await _ReadWholeScopeAsync(operations, scope, chunkSize: 10);
        var nextMark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value;
        var next = await _ReadWholeScopeAsync(operations, scope with { HighWaterMark = nextMark }, chunkSize: 10);

        // then: running the Operation again touches nothing; the next Operation sees the children and the late
        // failure, never a generation the first one already re-executed
        rerun.Should().BeEmpty();
        next.Select(static target => target.IncarnationId)
            .Should()
            .BeEquivalentTo(
                applied
                    .Select(static result => result.ChildIncarnationId!.Value)
                    .Append(late.InboxGeneration!.IncarnationId)
            );
    }

    public virtual async Task should_refuse_a_chunk_write_over_a_generation_another_node_already_re_executed()
    {
        // given: one node and another both read the same chunk
        var storage = GetStorage();
        var operations = storage.GetInboxOperationsApi();
        var consumer = $"scope-race-{Guid.NewGuid():N}";
        await _AdmitTerminalInboxAsync(storage, consumer);
        var scope = new MessageOperationScope
        {
            Kind = MessageOperationKind.ReExecute,
            Query = new MessageListQuery { MessageType = MessageType.Subscribe, ConsumerIdentity = consumer },
            HighWaterMark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value,
        };
        var chunk = await operations.ReadOperationScopeAsync(scope, afterId: null, chunkSize: 10, AbortToken);
        var target = chunk.Targets.Should().ContainSingle().Which;

        // when: each node writes the target under its own operation id
        var winner = await operations.ForceReprocessAsync(_OperatorRequest(target, "node-a"), AbortToken);
        var loser = await operations.ForceReprocessAsync(_OperatorRequest(target, "node-b"), AbortToken);

        // then: the second write is a refusal of a row that already moved on, not a failure
        winner.Outcome.Should().Be(InboxOperationOutcome.Applied);
        loser.Outcome.Should().Be(InboxOperationOutcome.StateConflict);
        loser.ChildIncarnationId.Should().BeNull();
        chunk.Next.Should().BeNull();
    }

    public virtual async Task should_leave_a_held_generation_out_of_a_delete_scope()
    {
        // given: two terminal generations, one of them held
        var storage = GetStorage();
        var operations = storage.GetInboxOperationsApi();
        var consumer = $"scope-delete-{Guid.NewGuid():N}";
        var kept = await _AdmitTerminalInboxAsync(storage, consumer);
        var deletable = await _AdmitTerminalInboxAsync(storage, consumer);
        var hold = await operations.HoldAsync(
            new InboxOperationRequest(
                Guid.NewGuid(),
                kept.InboxGeneration!.IncarnationId,
                StatusName.Failed,
                "keep for audit",
                _Operator("operator-hold")
            ),
            AbortToken
        );
        hold.Outcome.Should().Be(InboxOperationOutcome.Applied);
        var scope = new MessageOperationScope
        {
            Kind = MessageOperationKind.Delete,
            Query = new MessageListQuery { MessageType = MessageType.Subscribe, ConsumerIdentity = consumer },
            HighWaterMark = (await storage.GetLatestMessageChangeMarkerAsync(AbortToken))!.Value,
        };

        // when
        var targets = await _ReadWholeScopeAsync(operations, scope, chunkSize: 10);

        // then
        targets.Should().ContainSingle().Which.IncarnationId.Should().Be(deletable.InboxGeneration!.IncarnationId);
    }

    public virtual async Task should_read_the_ledger_newest_first_filtered_by_actor_including_refused_interventions()
    {
        // given: one operator's applied hold, then a refused repeat of it, and another operator's hold
        var storage = GetStorage();
        var operations = storage.GetInboxOperationsApi();
        var actor = $"ledger-{Guid.NewGuid():N}";
        var generation = await _AdmitTerminalInboxAsync(storage, $"ledger-consumer-{Guid.NewGuid():N}");
        var other = await _AdmitTerminalInboxAsync(storage, $"ledger-consumer-{Guid.NewGuid():N}");
        var request = new InboxOperationRequest(
            Guid.NewGuid(),
            generation.InboxGeneration!.IncarnationId,
            StatusName.Failed,
            "hold for review",
            _Operator(actor)
        );
        var applied = await operations.HoldAsync(request, AbortToken);
        var refused = await operations.HoldAsync(request with { OperationId = Guid.NewGuid() }, AbortToken);
        await operations.HoldAsync(
            request with
            {
                OperationId = Guid.NewGuid(),
                ExpectedIncarnationId = other.InboxGeneration!.IncarnationId,
                Authorization = _Operator(actor + "-other"),
            },
            AbortToken
        );
        var viewer = _Viewer();

        // when
        var ledger = await operations.QueryReceiptsAsync(
            new MessagingOperationReceiptQuery { Actor = actor, TargetKind = MessagingOperationTargetKind.Inbox },
            viewer,
            AbortToken
        );
        var firstPage = await operations.QueryReceiptsAsync(
            new MessagingOperationReceiptQuery { Actor = actor, Limit = 1 },
            viewer,
            AbortToken
        );
        var secondPage = await operations.QueryReceiptsAsync(
            new MessagingOperationReceiptQuery
            {
                Actor = actor,
                Limit = 1,
                After = firstPage.Next,
            },
            viewer,
            AbortToken
        );
        var scheduledOnly = await operations.QueryReceiptsAsync(
            new MessagingOperationReceiptQuery
            {
                Actor = actor,
                TargetKind = MessagingOperationTargetKind.ScheduledDelivery,
            },
            viewer,
            AbortToken
        );

        // then: both of the operator's interventions are in the ledger, the refused one included, newest first, and
        // paging one receipt at a time returns the same receipts in the same order
        applied.Outcome.Should().Be(InboxOperationOutcome.Applied);
        refused.Outcome.Should().Be(InboxOperationOutcome.StateConflict);
        ledger
            .Items.Select(static receipt => receipt.OperationId)
            .Should()
            .BeEquivalentTo([applied.OperationId, refused.OperationId]);
        ledger.Items.Should().BeInDescendingOrder(static receipt => receipt.CreatedAt);
        ledger
            .Items.Should()
            .ContainSingle(receipt => receipt.OperationId == refused.OperationId)
            .Which.Outcome.Should()
            .Be(InboxOperationOutcome.StateConflict);
        var appliedReceipt = ledger
            .Items.Should()
            .ContainSingle(receipt => receipt.OperationId == applied.OperationId)
            .Which;
        appliedReceipt.Outcome.Should().Be(InboxOperationOutcome.Applied);
        appliedReceipt.IncarnationId.Should().Be(generation.InboxGeneration.IncarnationId);
        appliedReceipt.Reason.Should().Be("hold for review");
        ledger.Items.Should().OnlyContain(receipt => receipt.Actor == actor);
        ledger.Items.Should().OnlyContain(static receipt => receipt.OperationType == MessagingOperationType.Hold);
        ledger.Items.Should().OnlyContain(static receipt => receipt.TargetKind == MessagingOperationTargetKind.Inbox);
        ledger.Next.Should().BeNull();
        firstPage.Items.Should().ContainSingle().Which.OperationId.Should().Be(ledger.Items[0].OperationId);
        firstPage.Next.Should().NotBeNull();
        secondPage.Items.Should().ContainSingle().Which.OperationId.Should().Be(ledger.Items[1].OperationId);
        secondPage.Next.Should().BeNull();
        scheduledOnly.Items.Should().BeEmpty();
    }

    public virtual async Task should_accept_an_empty_reason_for_a_single_hold_and_store_it_as_null()
    {
        // given
        var storage = GetStorage();
        var operations = storage.GetInboxOperationsApi();
        var actor = $"no-reason-{Guid.NewGuid():N}";
        var generation = await _AdmitTerminalInboxAsync(storage, $"no-reason-consumer-{Guid.NewGuid():N}");
        var request = new InboxOperationRequest(
            Guid.NewGuid(),
            generation.InboxGeneration!.IncarnationId,
            StatusName.Failed,
            "   ",
            _Operator(actor)
        );

        // when
        var result = await operations.HoldAsync(request, AbortToken);
        var replay = await operations.HoldAsync(request with { Reason = "" }, AbortToken);

        // then: the hold applies, the row, the receipt, and a replay all carry no reason
        result.Outcome.Should().Be(InboxOperationOutcome.Applied);
        result.Reason.Should().BeNull();
        replay.IsReplay.Should().BeTrue();
        replay.Outcome.Should().Be(InboxOperationOutcome.Applied);
        var view = await operations.QueryAsync(
            new InboxGenerationQuery { IncarnationId = generation.InboxGeneration.IncarnationId },
            _Viewer(),
            AbortToken
        );
        view.Items.Should().ContainSingle().Which.IsHeld.Should().BeTrue();
        view.Items[0].HoldReason.Should().BeNull();
        var ledger = await operations.QueryReceiptsAsync(
            new MessagingOperationReceiptQuery { Actor = actor },
            _Viewer(),
            AbortToken
        );
        ledger.Items.Should().ContainSingle().Which.Reason.Should().BeNull();
    }

    public virtual async Task should_serve_reads_to_a_viewer_without_a_named_operator_and_refuse_its_writes()
    {
        // given: a held generation, and a viewer authenticated without an operator name
        var storage = GetStorage();
        var operations = storage.GetInboxOperationsApi();
        var consumer = $"viewer-consumer-{Guid.NewGuid():N}";
        var held = await _AdmitTerminalInboxAsync(storage, consumer);
        var free = await _AdmitTerminalInboxAsync(storage, consumer);
        (
            await operations.HoldAsync(
                new InboxOperationRequest(
                    Guid.NewGuid(),
                    held.InboxGeneration!.IncarnationId,
                    StatusName.Failed,
                    Reason: null,
                    _Operator("viewer-test-operator")
                ),
                AbortToken
            )
        )
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        var viewer = _Viewer();

        // when
        var heldCount = await operations.CountAsync(
            new InboxGenerationQuery { ConsumerIdentity = consumer, IsHeld = true },
            viewer,
            cancellationToken: AbortToken
        );
        var heldList = await storage
            .GetMonitoringApi()
            .CountMessagesAsync(
                new MessageListQuery
                {
                    MessageType = MessageType.Subscribe,
                    ConsumerIdentity = consumer,
                    IsHeld = true,
                },
                cancellationToken: AbortToken
            );
        var scheduled = await storage
            .GetScheduledDeliveryOperationsApi()
            .QueryAsync(new ScheduledDeliveryQuery { MessageName = consumer }, viewer, AbortToken);
        var scheduledCount = await storage
            .GetScheduledDeliveryOperationsApi()
            .CountAsync(new ScheduledDeliveryQuery { MessageName = consumer }, viewer, cancellationToken: AbortToken);
        var write = async () =>
            await operations.HoldAsync(
                new InboxOperationRequest(
                    Guid.NewGuid(),
                    free.InboxGeneration!.IncarnationId,
                    StatusName.Failed,
                    "viewer tries to hold",
                    viewer
                ),
                AbortToken
            );

        // then
        heldCount.Should().Be(new MessageCount(1, Capped: false));
        heldList.Should().Be(new MessageCount(1, Capped: false));
        scheduled.Items.Should().BeEmpty();
        scheduledCount.Should().Be(new MessageCount(0, Capped: false));
        await write.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static async Task _FailReceivedAsync(IDataStorage storage, string consumer, string fingerprint)
    {
        var message = await storage.StoreReceivedMessageAsync("groups", consumer, CreateMessage(), AbortToken);
        message.FailureFingerprint = fingerprint;
        message.ExceptionInfo = "consumer failed";
        (
            await storage.ChangeReceiveStateAsync(
                message,
                StatusName.Failed,
                retryDelay: null,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();
    }

    private static async Task<MediumMessage> _AdmitTerminalInboxAsync(IDataStorage storage, string consumer)
    {
        var admitted = await _AdmitInboxAsync(
            storage,
            CreateMessage($"scope-{Guid.NewGuid():N}", "orders.created"),
            consumerIdentity: consumer
        );
        admitted.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        await _CompleteInboxForReplayAsync(storage, admitted.Message);

        return admitted.Message;
    }

    private static async Task _FailChildAsync(IDataStorage storage, InboxOperationResult result, MediumMessage parent)
    {
        var stored = await storage.GetMonitoringApi().GetReceivedMessageAsync(result.ChildStorageId!.Value, AbortToken);
        stored.Should().NotBeNull();

        // A fresh envelope, as a dispatcher holds one: the in-memory store returns its own row, which the attempt
        // write compares against.
        var child = new MediumMessage
        {
            StorageId = stored!.StorageId,
            Origin = stored.Origin,
            Content = stored.Content,
            Lane = stored.Lane,
            InboxKey = parent.InboxKey! with { Generation = result.ChildGeneration!.Value },
            InboxGeneration = new InboxGeneration(result.ChildGeneration.Value, result.ChildIncarnationId!.Value),
        };
        await _CompleteInboxForReplayAsync(storage, child);
    }

    private static async Task<List<MessageOperationTarget>> _ReadWholeScopeAsync(
        IInboxOperationsApi operations,
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

    private static InboxOperationRequest _OperatorRequest(MessageOperationTarget target, string actor) =>
        new(Guid.NewGuid(), target.IncarnationId, target.Status, "bulk re-execute", _Operator(actor));

    private static OperatorAuthorizationContext _Operator(string actor) =>
        new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, actor)], authenticationType: "test")));

    // Authenticated, but with no name a write could record as the operator.
    private static OperatorAuthorizationContext _Viewer() =>
        new(new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test")));
}
