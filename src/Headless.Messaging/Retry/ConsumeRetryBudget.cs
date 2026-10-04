// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Checks;
using Headless.Reliability;

namespace Headless.Messaging.Retry;

/// <summary>
/// The consume-side retry budget of one competing consumer, read from its resolved <see cref="FailurePolicyDefinition"/>.
/// </summary>
/// <remarks>
/// <para>
/// The tiers are additive. A received row's first dispatch (<c>Retries == 0</c>) gets the first attempt plus
/// <see cref="FailurePolicyDefinition.ImmediateRetries"/> back-to-back retries in the same dispatch. Each later
/// pickup is one delayed retry and gets exactly one attempt; <c>Retries</c> counts those pickups. A failing message
/// is therefore attempted at most <see cref="FailurePolicyDefinition.TotalAttempts"/> times.
/// </para>
/// <para>
/// The exhaustion decision, crash-recovery detection, and the in-flight state write all read the budget through this
/// type, so they cannot disagree about how many attempts a pickup still has.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ConsumeRetryBudget
{
    public ConsumeRetryBudget(FailurePolicyDefinition policy)
    {
        Policy = Argument.IsNotNull(policy);
    }

    public FailurePolicyDefinition Policy { get; }

    /// <summary>Returns how many back-to-back retries the dispatch of a row with <paramref name="retries"/> may run.</summary>
    public int GetInlineRetries(int retries) => retries == 0 ? Policy.ImmediateRetries : 0;

    /// <summary>Returns whether the current dispatch may retry again after <paramref name="inlineRetriesCompleted"/> retries.</summary>
    public bool HasMoreInlineAttempts(int retries, int inlineRetriesCompleted) =>
        inlineRetriesCompleted < GetInlineRetries(retries);

    /// <summary>Returns whether a row that has used <paramref name="retries"/> delayed retries may schedule another.</summary>
    public bool HasDelayedRetryLeft(int retries) => retries < Policy.DelayedRetries;

    /// <summary>
    /// Returns whether a row is already past its consumer's budget. A row exactly at the budget still gets its final
    /// delayed attempt; only a row beyond it (the policy shrank, or a crash loop advanced it) is.
    /// </summary>
    public bool IsOverBudget(int retries) => retries > Policy.DelayedRetries;

    /// <summary>
    /// Returns whether the durable attempt counter shows the dispatch already reserved its final attempt before the
    /// process died, so no fresh attempt may run.
    /// </summary>
    public bool IsFinalAttemptReserved(int retries, int reservedInlineAttempts) =>
        reservedInlineAttempts >= GetInlineRetries(retries) + 1;

    /// <summary>
    /// Decides what a retryable failure leads to: another attempt in this dispatch, a delayed retry persisted for a
    /// later pickup, or the end of the budget.
    /// </summary>
    /// <param name="retries">The delayed retries the row has used, before this failure.</param>
    /// <param name="inlineRetriesCompleted">The back-to-back retries this dispatch has already run.</param>
    public MessagingRetryDecision Decide(int retries, int inlineRetriesCompleted)
    {
        if (HasMoreInlineAttempts(retries, inlineRetriesCompleted))
        {
            return MessagingRetryDecision.Continue(TimeSpan.Zero);
        }

        return HasDelayedRetryLeft(retries)
            ? MessagingRetryDecision.Continue(Policy.GetDelayedRetryDelay(retries + 1))
            : MessagingRetryDecision.Exhausted;
    }
}
