// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// The one rule that decides what a record means for an attempt named by its generation, shared by the Core services
/// and every provider's renewal so they cannot disagree on when an attempt still owns its key.
/// </summary>
internal static class IdempotencyLeaseClassifier
{
    /// <summary>Classifies a record for the attempt that holds <paramref name="generation" />.</summary>
    /// <param name="status">The record's state.</param>
    /// <param name="recordGeneration">The record's generation, or <see langword="null" /> when no attempt holds it.</param>
    /// <param name="isLeaseLive">Whether the record's lease expiry is after the database clock.</param>
    /// <param name="generation">The attempt's generation.</param>
    /// <returns>What the attempt finds.</returns>
    public static IdempotentLeaseStatus Classify(
        IdempotencyRecordStatus status,
        long? recordGeneration,
        bool isLeaseLive,
        long generation
    )
    {
        if (status == IdempotencyRecordStatus.Completed)
        {
            // A completed record keeps the generation that completed it, so only that attempt learns it completed;
            // any other attempt was replaced before the completion.
            return recordGeneration == generation ? IdempotentLeaseStatus.Completed : IdempotentLeaseStatus.Stale;
        }

        if (recordGeneration is null)
        {
            return IdempotentLeaseStatus.Released;
        }

        if (recordGeneration != generation)
        {
            return IdempotentLeaseStatus.Stale;
        }

        return isLeaseLive ? IdempotentLeaseStatus.Current : IdempotentLeaseStatus.Expired;
    }
}
