// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Enums;

/// <summary>
/// Determines when a child time job is eligible to run relative to its parent's terminal status.
/// Set on <c>TimeJobEntity.RunCondition</c> when building chained job trees via
/// <c>FluentChainJobBuilder</c>.
/// </summary>
[PublicAPI]
public enum RunCondition
{
    /// <summary>
    /// Run only if the parent completed successfully
    /// (status is <c>Succeeded</c> or <c>DueDone</c>).
    /// </summary>
    OnSuccess = 0,

    /// <summary>Run only if the parent reached the <c>Failed</c> terminal state.</summary>
    OnFailure = 1,

    /// <summary>Run only if the parent reached the <c>Cancelled</c> terminal state.</summary>
    OnCancelled = 2,

    /// <summary>Run if the parent reached either <c>Failed</c> or <c>Cancelled</c>.</summary>
    OnFailureOrCancelled = 3,

    /// <summary>
    /// Run after the parent reaches any terminal state except <c>Skipped</c>
    /// (<c>Succeeded</c>, <c>DueDone</c>, <c>Failed</c>, <c>Cancelled</c>). Equivalent to
    /// <see langword="finally"/> semantics that excludes intentional skips.
    /// </summary>
    OnAnyCompletedStatus = 4,

    /// <summary>
    /// Run concurrently with the parent while the parent is <c>InProgress</c>. The child is
    /// dispatched at the same time as the parent rather than waiting for a terminal state.
    /// </summary>
    InProgress = 5,
}
