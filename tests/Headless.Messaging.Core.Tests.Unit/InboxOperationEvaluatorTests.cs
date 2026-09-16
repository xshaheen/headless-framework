// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Monitoring;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MessagingOperationEvaluatorTests : TestBase
{
    [Theory]
    [InlineData(MessagingOperationType.Hold, false, false, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.ReleaseHold, true, false, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.Purge, false, false, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.Purge, true, false, InboxOperationOutcome.Held)]
    [InlineData(MessagingOperationType.Hold, false, true, InboxOperationOutcome.Active)]
    [InlineData(MessagingOperationType.ReleaseHold, true, true, InboxOperationOutcome.Active)]
    [InlineData(MessagingOperationType.Purge, false, true, InboxOperationOutcome.Active)]
    [InlineData(MessagingOperationType.ForceReprocess, false, false, InboxOperationOutcome.Active)]
    [InlineData(MessagingOperationType.Cleanup, false, false, InboxOperationOutcome.Active)]
    [InlineData((MessagingOperationType)99, false, false, InboxOperationOutcome.Active)]
    public void should_limit_orphan_exception_to_safe_unclaimed_operations(
        MessagingOperationType operation,
        bool isHeld,
        bool hasLiveClaim,
        InboxOperationOutcome expected
    )
    {
        foreach (var status in new[] { StatusName.Scheduled, StatusName.Failed })
        {
            var state = new InboxOperationState(status, true, isHeld, true, 0, true, hasLiveClaim);
            MessagingOperationEvaluator.Evaluate(operation, status, state).Should().Be(expected);
        }
    }

    [Fact]
    public void should_return_not_found_for_missing_state()
    {
        MessagingOperationEvaluator
            .Evaluate(MessagingOperationType.Hold, StatusName.Failed, null)
            .Should()
            .Be(InboxOperationOutcome.NotFound);
    }

    [Fact]
    public void should_check_expected_status_before_activity_and_operation_guards()
    {
        var state = new InboxOperationState(StatusName.Scheduled, true, true, false, long.MaxValue);
        MessagingOperationEvaluator
            .Evaluate(MessagingOperationType.Purge, StatusName.Failed, state)
            .Should()
            .Be(InboxOperationOutcome.StateConflict);
    }

    [Theory]
    [InlineData(StatusName.Scheduled, false)]
    [InlineData(StatusName.Delayed, false)]
    [InlineData(StatusName.Queued, false)]
    [InlineData(StatusName.Failed, true)]
    [InlineData(StatusName.Succeeded, true)]
    public void should_check_activity_before_operation_guards(StatusName status, bool hasNextRetry)
    {
        var state = new InboxOperationState(status, hasNextRetry, true, false, long.MaxValue);
        MessagingOperationEvaluator
            .Evaluate(MessagingOperationType.Purge, status, state)
            .Should()
            .Be(InboxOperationOutcome.Active);
    }

    [Theory]
    [InlineData(MessagingOperationType.Hold, false, true, 0L, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.Hold, true, true, 0L, InboxOperationOutcome.StateConflict)]
    [InlineData(MessagingOperationType.Hold, false, false, long.MaxValue, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.ReleaseHold, true, false, long.MaxValue, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.ReleaseHold, false, true, 0L, InboxOperationOutcome.StateConflict)]
    [InlineData(MessagingOperationType.ForceReprocess, false, true, 0L, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.ForceReprocess, true, true, 0L, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.ForceReprocess, false, false, 0L, InboxOperationOutcome.StateConflict)]
    [InlineData(MessagingOperationType.ForceReprocess, false, true, long.MaxValue, InboxOperationOutcome.StateConflict)]
    [InlineData(MessagingOperationType.ForceReprocess, false, true, long.MaxValue - 1, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.Purge, true, true, 0L, InboxOperationOutcome.Held)]
    [InlineData(MessagingOperationType.Purge, false, false, long.MaxValue, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.Cleanup, true, false, long.MaxValue, InboxOperationOutcome.Applied)]
    [InlineData((MessagingOperationType)99, true, false, long.MaxValue, InboxOperationOutcome.Applied)]
    public void should_preserve_terminal_operation_policy(
        MessagingOperationType operation,
        bool isHeld,
        bool isCurrent,
        long generation,
        InboxOperationOutcome expected
    )
    {
        foreach (var status in new[] { StatusName.Succeeded, StatusName.Failed })
        {
            var state = new InboxOperationState(status, false, isHeld, isCurrent, generation);
            MessagingOperationEvaluator.Evaluate(operation, status, state).Should().Be(expected);
        }
    }
}
