// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Monitoring;
using Headless.Testing.Tests;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Tests;

public sealed class ScheduledDeliveryOperationEvaluatorTests : TestBase
{
    private static readonly DateTimeOffset _Now = DateTimeOffset.UtcNow;
    private const string _Version = "v1";

    [Theory]
    [InlineData(MessagingOperationType.Revoke, InboxOperationOutcome.Applied)]
    [InlineData(MessagingOperationType.DispatchNow, InboxOperationOutcome.Applied)]
    public void should_evaluate_applied_for_pending_delayed_row_with_matching_due_instant(
        MessagingOperationType operationType,
        InboxOperationOutcome expected
    )
    {
        var state = new ScheduledDeliveryOperationState(
            Status: StatusName.Delayed,
            InlineAttempts: 0,
            Retries: 0,
            NextRetryAt: null,
            HasLiveLease: false,
            ConfiguredVersion: _Version,
            MessageVersion: _Version,
            DueAt: _Now
        );

        MessagingOperationEvaluator.Evaluate(operationType, _Now, state).Should().Be(expected);
    }

    [Fact]
    public void should_evaluate_applied_for_revoke_on_leased_queued_row_and_active_for_dispatch_now()
    {
        var state = new ScheduledDeliveryOperationState(
            Status: StatusName.Queued,
            InlineAttempts: 0,
            Retries: 0,
            NextRetryAt: null,
            HasLiveLease: true,
            ConfiguredVersion: _Version,
            MessageVersion: _Version,
            DueAt: _Now
        );

        MessagingOperationEvaluator
            .Evaluate(MessagingOperationType.Revoke, _Now, state)
            .Should()
            .Be(InboxOperationOutcome.Applied);

        MessagingOperationEvaluator
            .Evaluate(MessagingOperationType.DispatchNow, _Now, state)
            .Should()
            .Be(InboxOperationOutcome.Active);
    }

    [Theory]
    [InlineData(MessagingOperationType.Revoke)]
    [InlineData(MessagingOperationType.DispatchNow)]
    public void should_evaluate_active_when_inline_attempt_is_reserved(MessagingOperationType operationType)
    {
        var state = new ScheduledDeliveryOperationState(
            Status: StatusName.Delayed,
            InlineAttempts: 1,
            Retries: 0,
            NextRetryAt: null,
            HasLiveLease: false,
            ConfiguredVersion: _Version,
            MessageVersion: _Version,
            DueAt: _Now
        );

        MessagingOperationEvaluator.Evaluate(operationType, _Now, state).Should().Be(InboxOperationOutcome.Active);
    }

    [Theory]
    [InlineData(MessagingOperationType.Revoke)]
    [InlineData(MessagingOperationType.DispatchNow)]
    public void should_evaluate_state_conflict_on_due_instant_mismatch(MessagingOperationType operationType)
    {
        var state = new ScheduledDeliveryOperationState(
            Status: StatusName.Delayed,
            InlineAttempts: 0,
            Retries: 0,
            NextRetryAt: null,
            HasLiveLease: false,
            ConfiguredVersion: _Version,
            MessageVersion: _Version,
            DueAt: _Now
        );

        var differentDue = _Now.AddMinutes(5);
        MessagingOperationEvaluator
            .Evaluate(operationType, differentDue, state)
            .Should()
            .Be(InboxOperationOutcome.StateConflict);
    }

    [Theory]
    [InlineData(MessagingOperationType.Revoke)]
    [InlineData(MessagingOperationType.DispatchNow)]
    public void should_evaluate_not_found_when_state_is_null(MessagingOperationType operationType)
    {
        MessagingOperationEvaluator.Evaluate(operationType, _Now, null).Should().Be(InboxOperationOutcome.NotFound);
    }

    [Theory]
    [InlineData(MessagingOperationType.Revoke)]
    [InlineData(MessagingOperationType.DispatchNow)]
    public void should_evaluate_not_found_for_terminal_retry_backlog_or_mismatched_version(
        MessagingOperationType operationType
    )
    {
        // Terminal row: Succeeded
        var succeeded = new ScheduledDeliveryOperationState(
            Status: StatusName.Succeeded,
            InlineAttempts: 0,
            Retries: 0,
            NextRetryAt: null,
            HasLiveLease: false,
            ConfiguredVersion: _Version,
            MessageVersion: _Version,
            DueAt: _Now
        );
        MessagingOperationEvaluator
            .Evaluate(operationType, _Now, succeeded)
            .Should()
            .Be(InboxOperationOutcome.NotFound);

        // Terminal row: Failed
        var failed = new ScheduledDeliveryOperationState(
            Status: StatusName.Failed,
            InlineAttempts: 0,
            Retries: 0,
            NextRetryAt: null,
            HasLiveLease: false,
            ConfiguredVersion: _Version,
            MessageVersion: _Version,
            DueAt: _Now
        );
        MessagingOperationEvaluator.Evaluate(operationType, _Now, failed).Should().Be(InboxOperationOutcome.NotFound);

        // Retry backlog: Retries > 0
        var retried = new ScheduledDeliveryOperationState(
            Status: StatusName.Delayed,
            InlineAttempts: 0,
            Retries: 1,
            NextRetryAt: null,
            HasLiveLease: false,
            ConfiguredVersion: _Version,
            MessageVersion: _Version,
            DueAt: _Now
        );
        MessagingOperationEvaluator.Evaluate(operationType, _Now, retried).Should().Be(InboxOperationOutcome.NotFound);

        // Retry backlog: NextRetryAt is set
        var pendingRetry = new ScheduledDeliveryOperationState(
            Status: StatusName.Delayed,
            InlineAttempts: 0,
            Retries: 0,
            NextRetryAt: _Now.AddMinutes(1),
            HasLiveLease: false,
            ConfiguredVersion: _Version,
            MessageVersion: _Version,
            DueAt: _Now
        );
        MessagingOperationEvaluator
            .Evaluate(operationType, _Now, pendingRetry)
            .Should()
            .Be(InboxOperationOutcome.NotFound);

        // Mismatched version
        var otherVersion = new ScheduledDeliveryOperationState(
            Status: StatusName.Delayed,
            InlineAttempts: 0,
            Retries: 0,
            NextRetryAt: null,
            HasLiveLease: false,
            ConfiguredVersion: _Version,
            MessageVersion: "v2",
            DueAt: _Now
        );
        MessagingOperationEvaluator
            .Evaluate(operationType, _Now, otherVersion)
            .Should()
            .Be(InboxOperationOutcome.NotFound);

        // Null due instant
        var nullDue = new ScheduledDeliveryOperationState(
            Status: StatusName.Delayed,
            InlineAttempts: 0,
            Retries: 0,
            NextRetryAt: null,
            HasLiveLease: false,
            ConfiguredVersion: _Version,
            MessageVersion: _Version,
            DueAt: null
        );
        MessagingOperationEvaluator.Evaluate(operationType, _Now, nullDue).Should().Be(InboxOperationOutcome.NotFound);
    }
}
