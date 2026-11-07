// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// One attempt of a run: a single execution of a time job (<see cref="TimeJobAttemptEntity"/>) or a cron occurrence
/// (<see cref="CronJobOccurrenceAttemptEntity"/>), with the node that ran it, its times, outcome, exception, and
/// failure fingerprint.
/// </summary>
/// <remarks>
/// <para>
/// The executor opens an attempt each time it invokes the handler, once per in-process retry, with its own write. The
/// open is best effort: a lost open never fails the run. Every write that ends an execution closes the open attempt
/// atomically with the write that ends it: the retry write, the terminal write, and every store write that moves the
/// run out of <c>InProgress</c> (a failure, skip, or hand-back after node death or a lapsed lease, and a graceful
/// stop). When no open attempt exists because its open was lost, the close inserts a closed attempt with what the
/// writer knows. Only the write that wins the run's conditional update closes its attempt, so an attempt never closes
/// twice.
/// </para>
/// <para>
/// Deleting a run deletes its attempts. The setters are public so a third-party persistence provider can write them.
/// </para>
/// </remarks>
[PublicAPI]
public abstract class JobAttemptEntity
{
    /// <summary>The identifier of the time job or cron occurrence this attempt belongs to.</summary>
    public Guid RunId { get; set; }

    /// <summary>The attempt's number within its run: 1 for the first attempt, increasing by one per attempt.</summary>
    public int Number { get; set; }

    /// <summary>The <c>cluster/node@incarnation</c> owner that ran the attempt, when known.</summary>
    public string? OwnerId { get; set; }

    /// <summary>
    /// When the executor invoked the handler (UTC), or <see langword="null"/> when the attempt's open was lost and the
    /// writer that closed it did not know.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>When the attempt ended (UTC); <see langword="null"/> while it runs.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>How the attempt ended; <see langword="null"/> while it runs.</summary>
    public JobAttemptOutcome? Outcome { get; set; }

    /// <summary>
    /// The exception text of a failed or cancelled attempt, or the reason the store gives when it ended the attempt
    /// itself (node not alive, lease lapsed, host stopped).
    /// </summary>
    public string? ExceptionMessage { get; set; }

    /// <summary>The failure fingerprint of a failed attempt; see <see cref="JobFailureFingerprints"/>.</summary>
    public string? FailureFingerprint { get; set; }
}

/// <summary>One attempt of a time job; <see cref="JobAttemptEntity.RunId"/> is the time job's identifier.</summary>
[PublicAPI]
public sealed class TimeJobAttemptEntity : JobAttemptEntity;

/// <summary>One attempt of a cron occurrence; <see cref="JobAttemptEntity.RunId"/> is the occurrence's identifier.</summary>
[PublicAPI]
public sealed class CronJobOccurrenceAttemptEntity : JobAttemptEntity;
