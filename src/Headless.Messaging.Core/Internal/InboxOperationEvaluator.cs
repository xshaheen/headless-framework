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

internal static class InboxOperationEvaluator
{
    public static InboxOperationOutcome Evaluate(
        InboxOperationType operationType,
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
            && operationType is InboxOperationType.Hold or InboxOperationType.ReleaseHold or InboxOperationType.Purge;
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
            InboxOperationType.Hold when row.IsHeld => InboxOperationOutcome.StateConflict,
            InboxOperationType.ReleaseHold when !row.IsHeld => InboxOperationOutcome.StateConflict,
            InboxOperationType.ForceReprocess when !row.IsCurrentGeneration || row.Generation == long.MaxValue =>
                InboxOperationOutcome.StateConflict,
            InboxOperationType.Purge when row.IsHeld => InboxOperationOutcome.Held,
            _ => InboxOperationOutcome.Applied,
        };
    }
}
