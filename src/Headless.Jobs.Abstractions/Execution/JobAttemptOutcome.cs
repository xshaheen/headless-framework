// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>How one attempt of a time job or cron occurrence ended; see <see cref="JobAttemptEntity"/>.</summary>
/// <remarks>
/// New members may be added in future versions; consumers that <see langword="switch"/> on this enum should include a
/// default arm.
/// </remarks>
[PublicAPI]
public enum JobAttemptOutcome
{
    /// <summary>The handler returned without error, including a run that finished as <c>DueDone</c>.</summary>
    Succeeded = 0,

    /// <summary>
    /// The attempt failed: the handler threw (an in-process retry follows when budget remains), or the store failed the
    /// run because its node died, its lease lapsed, or its node stopped, under the <c>MarkFailed</c> node-death policy.
    /// </summary>
    Failed = 1,

    /// <summary>A durable cancellation request stopped the handler.</summary>
    Cancelled = 2,

    /// <summary>
    /// The node running the attempt died or stopped renewing its lease, and the store handed the run back to be
    /// claimed again under the <c>Retry</c> node-death policy.
    /// </summary>
    Lost = 3,

    /// <summary>
    /// The attempt ended as <c>Skipped</c>: the handler asked to skip, or the store skipped the run under the
    /// <c>Skip</c> node-death policy.
    /// </summary>
    Skipped = 4,

    /// <summary>The node stopped gracefully and handed the run back without charging its retry budget.</summary>
    Released = 5,
}
