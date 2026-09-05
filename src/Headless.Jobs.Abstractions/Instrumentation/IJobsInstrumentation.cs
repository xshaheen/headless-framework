// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs.Instrumentation;

/// <summary>
/// Simple placeholder interface for Jobs instrumentation
/// </summary>
internal interface IJobsInstrumentation
{
    Activity? StartJobActivity(string activityName, JobExecutionState context);
    void LogJobEnqueued(string jobType, string functionName, Guid jobId, string? enqueuedFrom = null);
    void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success);
    void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount);
    void LogJobCancelled(Guid jobId, string functionName, string reason);
    void LogJobSkipped(Guid jobId, string functionName, string reason);
    void LogSeedingDataStarted(string seedingDataType);
    void LogSeedingDataCompleted(string seedingDataType);

    /// <summary>
    /// Records that a cron definition's schedule fell behind and was resolved by a recovery policy.
    /// </summary>
    /// <param name="cronJobId">The definition that fell behind.</param>
    /// <param name="functionName">Its registered function name.</param>
    /// <param name="policy">The policy applied — the recovery's outcome, not merely its trigger.</param>
    /// <param name="missedCount">Occurrences counted as missed.</param>
    /// <param name="countIsLowerBound">
    /// Whether <paramref name="missedCount"/> saturated the evaluation ceiling. An operator reading "1000 missed" must
    /// be able to tell an exact count from "at least 1000" — reporting a saturated count as exact is the misreading
    /// this flag exists to prevent, and no consumer can recover the distinction afterwards.
    /// </param>
    /// <param name="earliestMissedUtc">First occurrence after the watermark; exact even when the count saturated.</param>
    /// <param name="latestMissedUtc">Last instant the bounded walk reached.</param>
    /// <param name="skippedOccurrenceCount">Pending occurrences the policy retired.</param>
    /// <remarks>
    /// The count and the latest instant are emitted here and deliberately never persisted: only what the job itself
    /// consumes lives on the occurrence row, and threading a count through every pickup, claim, and retry projection
    /// would be considerable work purely for reporting.
    /// </remarks>
    void LogCronRecoveryApplied(
        Guid cronJobId,
        string functionName,
        MissedRunPolicy policy,
        int missedCount,
        bool countIsLowerBound,
        DateTime earliestMissedUtc,
        DateTime latestMissedUtc,
        int skippedOccurrenceCount
    );

    /// <summary>
    /// Records that a definition was positioned under schedule-interpretation rules that have since changed, and was
    /// rebased under the current ones.
    /// </summary>
    /// <param name="cronJobId">The rebased definition.</param>
    /// <param name="functionName">Its registered function name.</param>
    /// <param name="previousFingerprint">The superseded fingerprint, or <see langword="null"/> for uninitialized definitions.</param>
    /// <param name="currentFingerprint">The fingerprint derived from the running evaluator.</param>
    /// <param name="previousReconciledThroughUtc">The durable watermark observed before rebasing.</param>
    /// <param name="rebaseAnchorUtc">The provider-time anchor used for the non-replay rebase.</param>
    /// <param name="previousNextDueUtc">The projection derived under the superseded rules.</param>
    /// <param name="rebasedNextDueUtc">The projection under current rules.</param>
    /// <remarks>
    /// Surfacing this is the entire point of the fingerprint: the expression and timezone are unchanged, so without
    /// this signal a schedule silently starts firing at a different instant with nothing in the definition to show it.
    /// </remarks>
    void LogCronFingerprintRebased(
        Guid cronJobId,
        string functionName,
        string? previousFingerprint,
        string currentFingerprint,
        DateTime previousReconciledThroughUtc,
        DateTime rebaseAnchorUtc,
        DateTime previousNextDueUtc,
        DateTime rebasedNextDueUtc
    );

    void LogRequestDeserializationFailure(
        string requestType,
        string functionName,
        Guid jobId,
        JobType type,
        Exception exception
    );
}
