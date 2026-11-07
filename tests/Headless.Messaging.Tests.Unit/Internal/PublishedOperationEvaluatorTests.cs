// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PublishedOperationEvaluatorTests : TestBase
{
    private static readonly DateTimeOffset _ChangedAt = new(2026, 11, 7, 3, 18, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(MessagingOperationType.Resend)]
    [InlineData(MessagingOperationType.Purge)]
    public void should_return_not_found_when_the_row_is_missing(MessagingOperationType operationType)
    {
        MessagingOperationEvaluator
            .EvaluatePublished(operationType, _ChangedAt, state: null)
            .Should()
            .Be(InboxOperationOutcome.NotFound);
    }

    [Theory]
    [InlineData(MessagingOperationType.Resend, StatusName.Failed)]
    [InlineData(MessagingOperationType.Purge, StatusName.Failed)]
    [InlineData(MessagingOperationType.Purge, StatusName.Succeeded)]
    public void should_return_state_conflict_when_the_change_marker_is_stale(
        MessagingOperationType operationType,
        StatusName status
    )
    {
        var state = _State(status);

        MessagingOperationEvaluator
            .EvaluatePublished(operationType, _ChangedAt.AddTicks(-1), state)
            .Should()
            .Be(InboxOperationOutcome.StateConflict);
    }

    [Theory]
    // A send in flight holds a live lease: the row is never touched under it.
    [InlineData(MessagingOperationType.Resend, StatusName.Failed, false, true)]
    [InlineData(MessagingOperationType.Purge, StatusName.Failed, false, true)]
    [InlineData(MessagingOperationType.Purge, StatusName.Succeeded, false, true)]
    // A pending retry means the pipeline still owns the row.
    [InlineData(MessagingOperationType.Resend, StatusName.Failed, true, false)]
    [InlineData(MessagingOperationType.Purge, StatusName.Failed, true, false)]
    // A row not yet terminal is still on its way out.
    [InlineData(MessagingOperationType.Resend, StatusName.Scheduled, false, false)]
    [InlineData(MessagingOperationType.Resend, StatusName.Delayed, false, false)]
    [InlineData(MessagingOperationType.Resend, StatusName.Queued, false, false)]
    [InlineData(MessagingOperationType.Purge, StatusName.Scheduled, false, false)]
    [InlineData(MessagingOperationType.Purge, StatusName.Delayed, false, false)]
    [InlineData(MessagingOperationType.Purge, StatusName.Queued, false, false)]
    public void should_return_active_when_the_row_is_still_in_flight(
        MessagingOperationType operationType,
        StatusName status,
        bool hasNextRetry,
        bool hasLiveLease
    )
    {
        var state = _State(status, hasNextRetry, hasLiveLease);

        MessagingOperationEvaluator
            .EvaluatePublished(operationType, _ChangedAt, state)
            .Should()
            .Be(InboxOperationOutcome.Active);
        MessagingOperationEvaluator
            .EvaluatePublished(operationType, expectedChangedAt: null, state)
            .Should()
            .Be(InboxOperationOutcome.Active);
    }

    [Theory]
    [InlineData(MessagingOperationType.Resend, StatusName.Succeeded, InboxOperationOutcome.StateConflict)]
    [InlineData(MessagingOperationType.Resend, StatusName.Failed, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.Purge, StatusName.Succeeded, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.Purge, StatusName.Failed, InboxOperationOutcome.Applied)]
    public void should_resend_only_failures_and_delete_either_terminal_status(
        MessagingOperationType operationType,
        StatusName status,
        InboxOperationOutcome expected
    )
    {
        var state = _State(status);

        MessagingOperationEvaluator.EvaluatePublished(operationType, _ChangedAt, state).Should().Be(expected);
        MessagingOperationEvaluator
            .EvaluatePublished(operationType, expectedChangedAt: null, state)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData(MessagingOperationType.Hold)]
    [InlineData(MessagingOperationType.ForceReprocess)]
    [InlineData(MessagingOperationType.Revoke)]
    public void should_refuse_an_operation_a_published_row_does_not_take(MessagingOperationType operationType)
    {
        MessagingOperationEvaluator
            .EvaluatePublished(operationType, _ChangedAt, _State(StatusName.Failed))
            .Should()
            .Be(InboxOperationOutcome.StateConflict);
    }

    private static PublishedOperationState _State(
        StatusName status,
        bool hasNextRetry = false,
        bool hasLiveLease = false
    ) => new(status, hasNextRetry, hasLiveLease, _ChangedAt);
}
