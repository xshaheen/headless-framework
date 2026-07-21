// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Internal;

/// <summary>
/// U5/KTD3 timed-descendant gating rules, extracted from the executor's <c>_ShouldRunChild</c> mapping so the
/// claim gate, the release/skip reconcile, and the in-process executor all decide "does the parent's terminal
/// state satisfy this child's <see cref="RunCondition"/>?" the same way. Relational providers cannot call these
/// inside a translated query — they re-express the identical predicate in LINQ/SQL — but the in-memory provider,
/// the manager, and unit tests share this single source of truth.
/// </summary>
internal static class ChainRunConditionRules
{
    /// <summary>
    /// The terminal states a running/succeeded/failed/cancelled/skipped job can settle into; a
    /// non-terminal parent is never a gate target.
    /// </summary>
    public static bool IsTerminal(JobStatus status) =>
        status
            is JobStatus.Succeeded
                or JobStatus.DueDone
                or JobStatus.Failed
                or JobStatus.Cancelled
                or JobStatus.Skipped;

    /// <summary>
    /// <see langword="true"/> for the run conditions whose timed descendant must wait for a specific parent
    /// terminal state before it may be claimed. <see cref="RunCondition.InProgress"/> and a <see langword="null"/>
    /// condition are intentionally NOT gated — they keep their pre-#311 behavior (run concurrently / run at time).
    /// </summary>
    public static bool IsParentTerminalGated(RunCondition? runCondition) =>
        runCondition
            is RunCondition.OnSuccess
                or RunCondition.OnFailure
                or RunCondition.OnCancelled
                or RunCondition.OnFailureOrCancelled
                or RunCondition.OnAnyCompletedStatus;

    /// <summary>
    /// Whether a parent that reached <paramref name="parentStatus"/> satisfies the child's
    /// <paramref name="runCondition"/> — the terminal arms of the executor's <c>_ShouldRunChild</c>. Only
    /// meaningful when <paramref name="parentStatus"/> is terminal.
    /// </summary>
    public static bool ParentTerminalMatches(RunCondition? runCondition, JobStatus parentStatus) =>
        runCondition switch
        {
            RunCondition.OnSuccess => parentStatus is JobStatus.Succeeded or JobStatus.DueDone,
            RunCondition.OnFailure => parentStatus is JobStatus.Failed,
            RunCondition.OnCancelled => parentStatus is JobStatus.Cancelled,
            RunCondition.OnFailureOrCancelled => parentStatus is JobStatus.Failed or JobStatus.Cancelled,
            RunCondition.OnAnyCompletedStatus => parentStatus
                is JobStatus.Succeeded
                    or JobStatus.DueDone
                    or JobStatus.Failed
                    or JobStatus.Cancelled,
            _ => false,
        };
}
