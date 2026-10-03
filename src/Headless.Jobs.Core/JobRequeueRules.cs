// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>
/// The requeue eligibility rule for time jobs, shared by every provider so they refuse the same rows for the same
/// reason and in the same order.
/// </summary>
internal static class JobRequeueRules
{
    /// <summary>
    /// The refusal for a time job in the given state, or <see langword="null"/> when it may be requeued.
    /// </summary>
    /// <param name="status">The row's current status.</param>
    /// <param name="isChainMember">Whether the row has a parent or at least one child.</param>
    /// <param name="businessKey">The row's business key; <see langword="null"/> for an ordinary job.</param>
    /// <param name="isCurrentGeneration">Whether a keyed row is its key's current generation.</param>
    /// <remarks>
    /// Chain members are refused because a requeued child waits for a parent run that never comes, and a requeued
    /// parent re-runs against children that already resolved against its failure. A superseded keyed generation is
    /// refused because it would run beside the key's current generation.
    /// </remarks>
    internal static JobRequeueOutcome? RefuseTimeJob(
        JobStatus status,
        bool isChainMember,
        string? businessKey,
        bool? isCurrentGeneration
    )
    {
        if (status != JobStatus.Failed)
        {
            return JobRequeueOutcome.NotFailed;
        }

        if (isChainMember)
        {
            return JobRequeueOutcome.ChainMember;
        }

        if (businessKey is not null && isCurrentGeneration != true)
        {
            return JobRequeueOutcome.SupersededGeneration;
        }

        return null;
    }
}
