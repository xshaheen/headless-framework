// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Monitoring;
using Headless.Testing.Tests;

namespace Tests;

public sealed class InboxOperationEvaluatorTests : TestBase
{
    [Theory]
    [InlineData(InboxOperationType.Hold, false, false, InboxOperationOutcome.Applied)]
    [InlineData(InboxOperationType.ReleaseHold, true, false, InboxOperationOutcome.Applied)]
    [InlineData(InboxOperationType.Purge, false, false, InboxOperationOutcome.Applied)]
    [InlineData(InboxOperationType.Purge, true, false, InboxOperationOutcome.Held)]
    [InlineData(InboxOperationType.Hold, false, true, InboxOperationOutcome.Active)]
    [InlineData(InboxOperationType.ReleaseHold, true, true, InboxOperationOutcome.Active)]
    [InlineData(InboxOperationType.Purge, false, true, InboxOperationOutcome.Active)]
    [InlineData(InboxOperationType.ForceReprocess, false, false, InboxOperationOutcome.Active)]
    [InlineData(InboxOperationType.Cleanup, false, false, InboxOperationOutcome.Active)]
    [InlineData((InboxOperationType)99, false, false, InboxOperationOutcome.Active)]
    public void should_limit_orphan_exception_to_safe_unclaimed_operations(
        InboxOperationType operation,
        bool isHeld,
        bool hasLiveClaim,
        InboxOperationOutcome expected
    )
    {
        foreach (var status in new[] { StatusName.Scheduled, StatusName.Failed })
        {
            var state = new InboxOperationState(status, true, isHeld, true, 0, true, hasLiveClaim);
            InboxOperationEvaluator.Evaluate(operation, status, state).Should().Be(expected);
        }
    }

    [Fact]
    public void should_return_not_found_for_missing_state()
    {
        InboxOperationEvaluator
            .Evaluate(InboxOperationType.Hold, StatusName.Failed, null)
            .Should()
            .Be(InboxOperationOutcome.NotFound);
    }

    [Fact]
    public void should_check_expected_status_before_activity_and_operation_guards()
    {
        var state = new InboxOperationState(StatusName.Scheduled, true, true, false, long.MaxValue);
        InboxOperationEvaluator
            .Evaluate(InboxOperationType.Purge, StatusName.Failed, state)
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
        InboxOperationEvaluator
            .Evaluate(InboxOperationType.Purge, status, state)
            .Should()
            .Be(InboxOperationOutcome.Active);
    }

    [Theory]
    [InlineData(InboxOperationType.Hold, false, true, 0L, InboxOperationOutcome.Applied)]
    [InlineData(InboxOperationType.Hold, true, true, 0L, InboxOperationOutcome.StateConflict)]
    [InlineData(InboxOperationType.Hold, false, false, long.MaxValue, InboxOperationOutcome.Applied)]
    [InlineData(InboxOperationType.ReleaseHold, true, false, long.MaxValue, InboxOperationOutcome.Applied)]
    [InlineData(InboxOperationType.ReleaseHold, false, true, 0L, InboxOperationOutcome.StateConflict)]
    [InlineData(InboxOperationType.ForceReprocess, false, true, 0L, InboxOperationOutcome.Applied)]
    [InlineData(InboxOperationType.ForceReprocess, true, true, 0L, InboxOperationOutcome.Applied)]
    [InlineData(InboxOperationType.ForceReprocess, false, false, 0L, InboxOperationOutcome.StateConflict)]
    [InlineData(InboxOperationType.ForceReprocess, false, true, long.MaxValue, InboxOperationOutcome.StateConflict)]
    [InlineData(InboxOperationType.ForceReprocess, false, true, long.MaxValue - 1, InboxOperationOutcome.Applied)]
    [InlineData(InboxOperationType.Purge, true, true, 0L, InboxOperationOutcome.Held)]
    [InlineData(InboxOperationType.Purge, false, false, long.MaxValue, InboxOperationOutcome.Applied)]
    [InlineData(InboxOperationType.Cleanup, true, false, long.MaxValue, InboxOperationOutcome.Applied)]
    [InlineData((InboxOperationType)99, true, false, long.MaxValue, InboxOperationOutcome.Applied)]
    public void should_preserve_terminal_operation_policy(
        InboxOperationType operation,
        bool isHeld,
        bool isCurrent,
        long generation,
        InboxOperationOutcome expected
    )
    {
        foreach (var status in new[] { StatusName.Succeeded, StatusName.Failed })
        {
            var state = new InboxOperationState(status, false, isHeld, isCurrent, generation);
            InboxOperationEvaluator.Evaluate(operation, status, state).Should().Be(expected);
        }
    }
}
