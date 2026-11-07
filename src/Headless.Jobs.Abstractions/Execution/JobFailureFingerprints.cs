// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// The failure fingerprint a failed time job or cron occurrence stores in <c>FailureFingerprint</c>, and the fixed
/// fingerprints for failures that have no exception to compute one from.
/// </summary>
/// <remarks>
/// <para>
/// A fingerprint groups failures by cause, so a dashboard can show one item per cause instead of one per first
/// exception line, which often carries ids and values. Every fingerprint starts with the algorithm version
/// (<see cref="VersionPrefix"/>); a change to the algorithm is a new version, and rows written under an older one keep
/// their groups until they age out.
/// </para>
/// <para>
/// When a handler throws, the executor computes the fingerprint from the live exception with
/// <c>Headless.FailureFingerprint.Compute</c> (in <c>Headless.Extensions</c>), the algorithm every Headless feature
/// shares: the innermost exception type, after unwrapping <see cref="AggregateException"/> and
/// <see cref="System.Reflection.TargetInvocationException"/>, plus the top three stack frames outside <c>System.*</c>
/// and <c>Microsoft.*</c>, each as a method full name with no line number and with compiler-generated lambda and async
/// state-machine names reduced to the declaring method. The result is the SHA-256 of those lines, truncated to 16 hex
/// characters. The executor carries it on <see cref="JobExecutionState.FailureFingerprint"/>, and the store writes it
/// beside the exception text.
/// </para>
/// <para>
/// A failure the store writes itself has no exception, so it stores one of the fixed values below, one per cause, in
/// the same conditional update that ends the run. A third-party persistence provider writes the same values for the
/// same causes, so every store groups those failures alike. Each value equals
/// <c>Headless.FailureFingerprint.ForCause</c> of its cause name; the values stay constants here because a provider
/// writes them inside an expression tree (an EF Core <c>SetProperty</c>), and this package does not reference
/// <c>Headless.Extensions</c>.
/// </para>
/// </remarks>
[PublicAPI]
public static class JobFailureFingerprints
{
    /// <summary>The prefix every version-1 fingerprint starts with.</summary>
    public const string VersionPrefix = "v1:";

    /// <summary>The longest fingerprint any version may store; the column's maximum length.</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// The dead-node sweep ended a running <c>MarkFailed</c> run because the node that ran it left the cluster.
    /// </summary>
    public const string NodeNotAlive = VersionPrefix + "node-not-alive";

    /// <summary>
    /// The stalled-lease sweep ended a running <c>MarkFailed</c> run because its lease lapsed without renewal.
    /// </summary>
    public const string LeaseLapsed = VersionPrefix + "lease-lapsed";

    /// <summary>
    /// The node that ran a <c>MarkFailed</c> run stopped while the run was still in progress, and settled it as failed.
    /// </summary>
    public const string InterruptedByShutdown = VersionPrefix + "interrupted-by-shutdown";

    /// <summary>
    /// The executor refused to start a run because lease-lapse and node-death reclaims had already spent its retry
    /// budget on interrupted attempts.
    /// </summary>
    public const string RetryBudgetExhaustedByRecovery = VersionPrefix + "retry-budget-exhausted-by-recovery";
}
