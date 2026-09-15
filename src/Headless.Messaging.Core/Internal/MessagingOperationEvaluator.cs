// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Monitoring;

namespace Headless.Messaging.Internal;

internal readonly record struct InboxOperationState(
    StatusName Status,
    bool HasNextRetry,
    bool IsHeld,
    bool IsCurrentGeneration,
    long Generation,
    bool IsOrphaned = false,
    bool HasLiveClaim = false
);

internal readonly record struct ScheduledDeliveryOperationState(
    StatusName Status,
    int InlineAttempts,
    int Retries,
    DateTimeOffset? NextRetryAt,
    bool HasLiveLease,
    string ConfiguredVersion,
    string MessageVersion,
    DateTimeOffset? DueAt
);

internal static class MessagingOperationEvaluator
{
    public static InboxOperationOutcome Evaluate(
        MessagingOperationType operationType,
        StatusName expectedStatus,
        InboxOperationState? state
    )
    {
        if (state is not { } row)
        {
            return InboxOperationOutcome.NotFound;
        }

        if (row.Status != expectedStatus)
        {
            return InboxOperationOutcome.StateConflict;
        }

        var allowsUnclaimedOrphan =
            row.IsOrphaned
            && !row.HasLiveClaim
            && operationType
                is MessagingOperationType.Hold
                    or MessagingOperationType.ReleaseHold
                    or MessagingOperationType.Purge;
        if (
            row.HasLiveClaim
            || (
                !allowsUnclaimedOrphan
                && (row.Status is not (StatusName.Succeeded or StatusName.Failed) || row.HasNextRetry)
            )
        )
        {
            return InboxOperationOutcome.Active;
        }

        return operationType switch
        {
            MessagingOperationType.Hold when row.IsHeld => InboxOperationOutcome.StateConflict,
            MessagingOperationType.ReleaseHold when !row.IsHeld => InboxOperationOutcome.StateConflict,
            MessagingOperationType.ForceReprocess when !row.IsCurrentGeneration || row.Generation == long.MaxValue =>
                InboxOperationOutcome.StateConflict,
            MessagingOperationType.Purge when row.IsHeld => InboxOperationOutcome.Held,
            _ => InboxOperationOutcome.Applied,
        };
    }

    public static InboxOperationOutcome Evaluate(
        MessagingOperationType operationType,
        DateTimeOffset expectedDueAt,
        ScheduledDeliveryOperationState? state
    )
    {
        if (state is not { } row)
        {
            return InboxOperationOutcome.NotFound;
        }

        if (
            !string.Equals(row.MessageVersion, row.ConfiguredVersion, StringComparison.Ordinal)
            || row.Status is not (StatusName.Delayed or StatusName.Queued)
            || row.Retries > 0
            || row.NextRetryAt is not null
            || row.DueAt is null
        )
        {
            return InboxOperationOutcome.NotFound;
        }

        if (row.DueAt.Value != expectedDueAt)
        {
            return InboxOperationOutcome.StateConflict;
        }

        if (row.InlineAttempts > 0)
        {
            return InboxOperationOutcome.Active;
        }

        return operationType switch
        {
            MessagingOperationType.Revoke => InboxOperationOutcome.Applied,
            MessagingOperationType.DispatchNow => row.HasLiveLease
                ? InboxOperationOutcome.Active
                : InboxOperationOutcome.Applied,
            _ => InboxOperationOutcome.StateConflict,
        };
    }
}
