// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Operational-store SPI for the Jobs scheduler: the durable persistence contract a backend provider (for
/// example the Entity Framework Core store, or the built-in in-memory provider) implements to queue, claim,
/// lease, renew, and terminalize time jobs and cron occurrences. Applications do not call this directly — they
/// schedule work through <c>ITimeJobManager</c> / <c>ICronJobManager</c>, and the scheduler drives this
/// provider. Implementations own the atomicity and ownership fencing described on each member.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provider SPI, not application API.</b> This interface is a service-provider interface: it exists so a
/// storage backend can plug into the Jobs scheduler, and its shape is dictated by what the scheduler loop needs
/// rather than by what application code finds ergonomic. It is marked <c>[PublicAPI]</c> because third-party
/// backends must be able to implement it, not because applications are expected to consume it. Application code
/// that calls these members directly bypasses the manager-level validation, coordination, and cache-invalidation
/// that <c>ITimeJobManager</c> / <c>ICronJobManager</c> perform, and is on its own for ownership fencing.
/// </para>
/// <para>
/// <b>Additive-only evolution policy.</b> Expect this interface to grow: the scheduler's needs are still
/// settling, and new capabilities (new sweeps, new projections, new claim shapes) land here first. New members
/// are added as <b>default interface methods</b> wherever a correct — if unoptimized — implementation can be
/// expressed in terms of the members already on this interface, so an existing implementer keeps compiling and
/// keeps working without edits. <see cref="GetCronOccurrenceGraphStatusCountsAsync"/> is the reference example:
/// the default projects through <see cref="GetAllCronJobOccurrencesAsync"/>, and providers override it only to
/// push the aggregation into storage. Where no such fallback exists — anything that requires an atomic,
/// storage-level claim or fence that cannot be composed from existing members without losing atomicity — the
/// member is added as abstract and is a breaking change for implementers, called out in the release notes.
/// Implementers should therefore treat "a new optional member appeared" as the normal case and review each
/// release's notes for the rarer abstract additions. Existing members are not renamed, reordered, or
/// semantically redefined; a semantic change ships as a new member instead.
/// </para>
/// <para>
/// <b>Ownership and fencing vocabulary</b> used throughout the member docs:
/// <list type="bullet">
/// <item><description>
/// <i>Owner</i> — the <c>node@incarnation</c> identity of the node holding a row, stored in <c>OwnerId</c>.
/// </description></item>
/// <item><description>
/// <i>Lease</i> — <c>LockedUntil</c>, the UTC instant after which another node may reclaim the row. Providers
/// stamp it from their own time authority: the injected <see cref="TimeProvider"/> for in-memory storage, the
/// database clock for relational storage.
/// </description></item>
/// <item><description>
/// <i>Completion fence</i> — an update applies only while this node still owns the row AND the row is still
/// non-terminal (<c>Idle</c>, <c>Queued</c>, or <c>InProgress</c>), so a late completion cannot clobber a row
/// that a sweep already reclaimed or terminalized.
/// </description></item>
/// <item><description>
/// <i>Acquirable</i> — the row is <c>Idle</c> or <c>Queued</c> and either unowned or past its lease, so this
/// node may claim it.
/// </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Cancellation.</b> Every member honours its <c>cancellationToken</c> and surfaces cancellation as
/// <see cref="OperationCanceledException"/>. Providers do not swallow it. Storage faults surface as the backing
/// store's own exception type (for example EF Core's <c>DbUpdateException</c> for the relational provider);
/// those types are deliberately not named in these docs because this package takes no dependency on any
/// storage stack.
/// </para>
/// </remarks>
/// <typeparam name="TTimeJob">The application's concrete time job entity type.</typeparam>
/// <typeparam name="TCronJob">The application's concrete cron job entity type.</typeparam>
[PublicAPI]
public interface IJobPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    #region Time_Ticker_Core_Methods

    /// <summary>
    /// Claims the supplied due time jobs for this node on the main scheduler path: each row that is still
    /// acquirable and unchanged since <paramref name="timeJobs"/> was read is stamped with this node's owner id,
    /// a fresh lease, and <c>Queued</c> status. Idle descendants of a claimed root are claimed alongside it so a
    /// job chain does not fragment across nodes.
    /// </summary>
    /// <param name="timeJobs">Candidate jobs previously read via <see cref="GetEarliestTimeJobsAsync"/>.</param>
    /// <param name="cancellationToken">Token that aborts the claim sweep between rows.</param>
    /// <returns>
    /// The jobs this node actually won, streamed as each claim commits. Rows lost to a concurrent claimer or
    /// changed since they were read are silently omitted — the caller must execute only what is yielded.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    IAsyncEnumerable<TimeJobEntity> QueueTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Fallback claim sweep for time jobs the main scheduler path missed: claims acquirable rows whose execution
    /// time is more than one second in the past, oldest first, in a capped batch. This is the safety net that
    /// recovers work stranded by a node that dropped out between reading and claiming.
    /// </summary>
    /// <param name="cancellationToken">Token that aborts the sweep between rows.</param>
    /// <returns>
    /// The overdue jobs this node actually won, streamed as each claim commits, stamped <c>Queued</c> with a
    /// fresh lease. Rows lost to a concurrent claimer are omitted.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    IAsyncEnumerable<TimeJobEntity> QueueTimedOutTimeJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases time jobs this node claimed but will not execute, returning each to <c>Idle</c> with its owner and
    /// lease cleared so another node can pick it up. Used on graceful shutdown and when dispatch is abandoned
    /// after a successful claim.
    /// </summary>
    /// <param name="timeJobIds">
    /// The jobs to release. An <b>empty</b> array is not a no-op: it releases every row this node currently holds
    /// in a releasable state.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the release.</param>
    /// <returns>A task that completes when the release has been applied.</returns>
    /// <remarks>
    /// Best-effort and silent: rows already terminalized, reclaimed, or owned by another node are skipped, and
    /// the count is not reported. The relational provider no-ops entirely when coordination membership is not
    /// established and it therefore has no owner identity to fence on.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task ReleaseAcquiredTimeJobsAsync(Guid[] timeJobIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads — without claiming — the batch of acquirable time jobs due in the earliest pending one-second
    /// bucket. This is the scheduler's peek: it decides what to attempt next and how long to sleep, then claims
    /// the result via <see cref="QueueTimeJobsAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>
    /// Every acquirable job whose execution time falls inside the earliest pending second, ordered by execution
    /// time, with the child hierarchy attached; empty when nothing is due. No ownership or status is mutated, so
    /// two nodes can observe the same batch — the claim step arbitrates.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<TimeJobEntity[]> GetEarliestTimeJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a single time job's execution outcome — status, timings, retry count, exception or skip reason —
    /// through the completion fence, persisting only the members named in
    /// <see cref="JobExecutionState.PropertiesToUpdate"/>.
    /// </summary>
    /// <param name="functionContext">
    /// The execution state to stamp; <see cref="JobExecutionState.JobId"/> selects the row.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the update.</param>
    /// <returns>
    /// <c>1</c> when the completion was applied, or <c>0</c> when the fence excluded the row because a sweep
    /// already reclaimed or terminalized it, the owner changed, or the row no longer exists. <c>0</c> is a
    /// normal outcome, not an error — the caller must not retry it as a failure.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> UpdateTimeJobAsync(JobExecutionState functionContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the serialized request payload for a time job, deserialized by the caller into the function's
    /// declared request type.
    /// </summary>
    /// <param name="id">Identifier of the time job.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>
    /// The stored payload bytes, or an <b>empty array</b> when the job carries no payload or does not exist.
    /// Never <see langword="null"/>, and a missing job is not distinguishable from an empty payload.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<byte[]> GetTimeJobRequestAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically requests cooperative cancellation by time-job ID. Idle jobs become terminal Cancelled immediately;
    /// Queued and InProgress jobs retain their status and set <c>CancelRequested</c>. Duplicate, terminal, and unknown
    /// requests return <see langword="false"/> without changing audit state.
    /// </summary>
    /// <param name="jobId">Identifier of the time job to cancel.</param>
    /// <param name="cancellationToken">Token that aborts the cancellation request itself (not the job).</param>
    /// <returns>
    /// <see langword="true"/> when this call was the one that recorded the cancellation request;
    /// <see langword="false"/> when the job is unknown, already terminal, or already had a cancellation recorded.
    /// </returns>
    /// <remarks>
    /// Cancelling an Idle job also propagates down its child chain: children whose run condition admits a
    /// cancelled parent are released to run, and the rest of the branch is stamped <c>Skipped</c>.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<bool> RequestTimeJobCancellationAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads durable cancellation through the provider's current ownership fence. Returns <see langword="true"/> or
    /// <see langword="false"/> only while this node still owns an InProgress row; <see langword="null"/> means the row
    /// is absent, reclaimed, terminal, or owned by another node.
    /// </summary>
    /// <param name="jobId">Identifier of the running time job to poll.</param>
    /// <param name="cancellationToken">Token that aborts the poll.</param>
    /// <returns>
    /// <see langword="true"/> when cancellation was requested for a row this node still owns and is still running;
    /// <see langword="false"/> when it was not; <see langword="null"/> when the fence does not hold. Callers treat
    /// <see langword="null"/> as lease loss, which is a stronger signal than a plain cancellation request.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<bool?> IsTimeJobCancellationRequestedAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="functionContext"/> to still-owned time jobs and returns the IDs that were actually
    /// stamped. Callers must execute only the returned IDs.
    /// </summary>
    /// <param name="timeJobIds">The claimed jobs to stamp with the shared execution state.</param>
    /// <param name="functionContext">
    /// The state to apply to every listed job; only the members named in
    /// <see cref="JobExecutionState.PropertiesToUpdate"/> are persisted.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the batch.</param>
    /// <returns>
    /// The subset of <paramref name="timeJobIds"/> that passed the fence and were stamped, in no guaranteed
    /// order. Empty when none survived.
    /// </returns>
    /// <remarks>
    /// This is also the claim-to-start recheck. When <paramref name="functionContext"/> carries an
    /// <c>InProgress</c> status the row must additionally still be <c>Queued</c>, so a duplicate scheduler
    /// wrapper cannot re-validate a job that is already running.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<Guid[]> UpdateTimeJobsWithUnifiedContextAsync(
        Guid[] timeJobIds,
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Claims specific time jobs for immediate out-of-band execution — the on-demand "run this now" path — taking
    /// them straight to <c>InProgress</c> with a fresh lease rather than through the <c>Queued</c> hand-off.
    /// </summary>
    /// <param name="ids">
    /// The jobs to claim. An empty array returns an empty result without touching storage.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the claim.</param>
    /// <returns>
    /// Only the jobs this node actually claimed, with unscheduled children attached for chain execution. Rows that
    /// were not acquirable — terminal, or leased by another node — are omitted, so an empty result means the
    /// request was fully lost and nothing should be executed.
    /// </returns>
    /// <remarks>
    /// The relational provider returns empty when coordination membership is not established, because it has no
    /// owner identity to stamp.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<TimeJobEntity[]> AcquireImmediateTimeJobsAsync(Guid[] ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Slides the running time job's lease forward (<c>LockedUntil = now + LeaseDuration</c>), fenced on
    /// current ownership + non-terminal status (#316/KTD3). Returns the affected row count: <c>1</c> when the
    /// lease was renewed, <c>0</c> when the lease was lost (reclaimed, owner changed, or terminalized) — the
    /// caller treats <c>0</c> as cancel-on-loss — or a <b>negative</b> value when coordination membership is not
    /// currently established (#461), which the caller treats as "skip this renewal tick", not loss.
    /// </summary>
    /// <param name="jobId">Identifier of the job whose lease is being renewed.</param>
    /// <param name="cancellationToken">Token that aborts the renewal.</param>
    /// <returns>
    /// <c>1</c> renewed, <c>0</c> lease lost, negative membership-unavailable. The three-way result is the whole
    /// point of the member: an implementation that collapses the negative case into <c>0</c> will cause running
    /// jobs to be cancelled during a transient membership blip.
    /// </returns>
    /// <remarks>
    /// Renewal slides a <b>running</b> lease only. Extending an <c>Idle</c> or <c>Queued</c> row would read as
    /// "lease held" and suppress cancel-on-loss, so those rows must return <c>0</c>. This UPDATE is itself the
    /// loss detector — implementations must not answer from a separate liveness query.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> RenewTimeJobLeaseAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims time jobs stuck <c>InProgress</c> whose lease lapsed (<c>LockedUntil &lt;= now</c>), independent of
    /// node death (#316/U3 — the gap-closer). Applies the same per-<c>OnNodeDeath</c> transitions as the dead-node
    /// sweep: <c>Retry</c> → released to <c>Idle</c> (re-claimable), <c>MarkFailed</c> → <c>Failed</c>, <c>Skip</c> →
    /// <c>Skipped</c>. A healthy renewing job keeps a future lease and is never matched. Returns the affected count.
    /// </summary>
    /// <param name="cancellationToken">Token that aborts the sweep.</param>
    /// <returns>The number of rows transitioned by this sweep; <c>0</c> when nothing had stalled.</returns>
    /// <remarks>
    /// Deliberately <b>not</b> owner-scoped: the trigger is a lapsed lease on any node, not a declared node death,
    /// which is what makes this the gap-closer for a node that vanished without being marked dead. Failed and
    /// skipped rows are stamped with a lease-lapse reason for diagnostics.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> ReclaimStalledTimeJobsAsync(CancellationToken cancellationToken = default);
    #endregion

    #region Cron_Ticker_Core_Methods

    /// <summary>
    /// Seeds and reconciles the cron definitions declared in code (via <c>[JobFunction]</c>) into durable storage
    /// at startup: inserts rows for newly declared functions, updates the expression in place when it changed, and
    /// deletes previously seeded rows — together with their occurrences — whose function no longer exists in code.
    /// </summary>
    /// <param name="cronJobs">The function name and cron expression of every cron function discovered in code.</param>
    /// <param name="cancellationToken">Token that aborts the migration.</param>
    /// <returns>A task that completes when the seed reconciliation has been persisted.</returns>
    /// <remarks>
    /// Rows are keyed by an identifier derived deterministically from the function name, so a re-seed updates the
    /// same row rather than inserting a duplicate, and concurrent seeding by several nodes converges on one row.
    /// The relational provider treats a lost insert race as benign — the winner's row stands, so the redundant
    /// insert is discarded and logged at <c>Debug</c> rather than thrown. Cron definitions created by the
    /// application at runtime are not seeded rows and are never touched by this reconciliation.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task MigrateDefinedCronJobsAsync(
        (string Function, string Expression)[] cronJobs,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Loads every cron definition so the scheduler can compute the next occurrence for each expression. This is
    /// the hottest read on the cron path — it runs on every scheduler tick.
    /// </summary>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>All cron definitions currently stored; empty when none are defined.</returns>
    /// <remarks>
    /// Because of its tick-rate call frequency the relational provider serves this from the configured
    /// <c>ICache</c> when one is registered, and invalidates that entry from every cron write path. A provider
    /// that caches must never persist a <see langword="null"/> or empty entry: an empty hit is indistinguishable
    /// from a genuinely empty cron table and would silently suspend all cron scheduling until the entry expires.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<CronJobEntity[]> GetAllCronJobExpressionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers the time jobs held by a node that coordination has declared dead, applying each row's
    /// <c>OnNodeDeath</c> policy: <c>Retry</c> → released to <c>Idle</c>, <c>MarkFailed</c> → <c>Failed</c>,
    /// <c>Skip</c> → <c>Skipped</c>.
    /// </summary>
    /// <param name="instanceIdentifier">
    /// The dead node's <c>node@incarnation</c> owner identity. Only rows owned by exactly this identity are
    /// considered, so a restarted node's new incarnation is never swept by its predecessor's cleanup.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the sweep.</param>
    /// <returns>The number of rows transitioned; <c>0</c> when the dead node held nothing recoverable.</returns>
    /// <remarks>
    /// <c>Idle</c> and <c>Queued</c> rows are reclaimed immediately — they were never executing, so nothing can be
    /// in flight. <c>InProgress</c> rows are reclaimed only once their lease has also lapsed, so a job that is
    /// genuinely still running survives a transient membership blip and is left to
    /// <see cref="ReclaimStalledTimeJobsAsync"/> if it truly died.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> ReleaseDeadNodeTimeJobResourcesAsync(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    );
    #endregion

    #region Cron_TickerOccurrence_Core_Methods

    /// <summary>
    /// Reads — without claiming — the single earliest acquirable occurrence within the main scheduler's
    /// one-second window, restricted to the supplied cron definitions. The cron counterpart of
    /// <see cref="GetEarliestTimeJobsAsync"/>: the scheduler uses it to decide what to attempt next.
    /// </summary>
    /// <param name="ids">
    /// Cron definition identifiers to restrict the search to. An empty array searches all definitions on
    /// providers that support it.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>
    /// The earliest acquirable occurrence with its cron definition attached, or <see langword="null"/> when
    /// nothing is available in the window. <b>The return is nullable in practice despite the non-nullable
    /// declared type</b> — this is a known wart in the current SPI shape; callers must null-check.
    /// </returns>
    /// <remarks>
    /// Heavily overdue occurrences are excluded here by design; they are recovered by
    /// <see cref="QueueTimedOutCronJobOccurrencesAsync"/>. The relational provider also returns
    /// <see langword="null"/> when coordination membership is not established.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<CronJobOccurrenceEntity<TCronJob>> GetEarliestAvailableCronOccurrenceAsync(
        Guid[] ids,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Materializes and claims the occurrences due at one scheduled instant: creates the occurrence row for each
    /// cron definition that does not yet have one for that instant, re-claims the row when it already exists, and
    /// stamps this node's owner id, a fresh lease, and <c>Queued</c> status.
    /// </summary>
    /// <param name="cronJobOccurrences">
    /// The scheduled instant (<c>Key</c>) and the cron definitions due at it (<c>Items</c>), each carrying the
    /// definition's node-death policy and, when known, the identity of the already-created occurrence row.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the sweep between definitions.</param>
    /// <returns>
    /// The occurrences this node actually created or won, streamed as each write commits. Rows lost to a
    /// concurrent scheduler are omitted.
    /// </returns>
    /// <remarks>
    /// Creation is deduplicated at the storage level by <c>(ExecutionTime, CronJobId)</c> rather than by a coarse
    /// distributed lock — storage dedup is the correctness boundary, and a lock would only serialize independent
    /// occurrences. The node-death policy is re-stamped from the definition on every claim so a policy change
    /// takes effect on re-queue instead of being pinned at row creation.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueCronJobOccurrencesAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Fallback claim sweep for cron occurrences the main scheduler path missed: claims acquirable occurrences
    /// whose execution time is more than one second in the past, oldest first, in a capped batch. The cron
    /// counterpart of <see cref="QueueTimedOutTimeJobsAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Token that aborts the sweep between rows.</param>
    /// <returns>
    /// The overdue occurrences this node actually won, streamed as each claim commits, stamped <c>Queued</c> with
    /// a fresh lease.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> QueueTimedOutCronJobOccurrencesAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes a single cron occurrence's execution outcome through the completion fence — the cron mirror of
    /// <see cref="UpdateTimeJobAsync"/> — persisting only the members named in
    /// <see cref="JobExecutionState.PropertiesToUpdate"/>.
    /// </summary>
    /// <param name="functionContext">
    /// The execution state to stamp; <see cref="JobExecutionState.JobId"/> selects the occurrence row.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the update.</param>
    /// <returns>
    /// <c>1</c> when the completion was applied, or <c>0</c> when the fence excluded the row because the owner is
    /// foreign or the status is already terminal. The count deliberately mirrors
    /// <see cref="UpdateTimeJobAsync"/> so the cron fence is observable and testable rather than silent.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> UpdateCronJobOccurrenceAsync(
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Releases cron occurrences this node claimed but will not execute, returning each to <c>Idle</c> with its
    /// owner and lease cleared. The cron mirror of <see cref="ReleaseAcquiredTimeJobsAsync"/>.
    /// </summary>
    /// <param name="occurrenceIds">
    /// The occurrences to release. An <b>empty</b> array is not a no-op: it releases every occurrence this node
    /// currently holds in a releasable state.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the release.</param>
    /// <returns>A task that completes when the release has been applied.</returns>
    /// <remarks>
    /// Best-effort and silent: rows already terminalized, reclaimed, or foreign-owned are skipped and the count is
    /// not reported. The relational provider no-ops entirely when it has no owner identity to fence on.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task ReleaseAcquiredCronJobOccurrencesAsync(Guid[] occurrenceIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the serialized request payload for a cron occurrence. Occurrences carry no payload of their own — the
    /// payload lives on the owning cron definition, and this member resolves it through that relationship.
    /// </summary>
    /// <param name="jobId">Identifier of the cron <i>occurrence</i> (not of the cron definition).</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>
    /// The owning definition's payload bytes, or an <b>empty array</b> when there is no payload or the occurrence
    /// does not exist. Never <see langword="null"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<byte[]> GetCronJobOccurrenceRequestAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="functionContext"/> to still-owned cron occurrences and returns the IDs that were
    /// actually stamped. Callers must execute only the returned IDs.
    /// </summary>
    /// <param name="timeJobIds">The claimed occurrence identifiers to stamp with the shared execution state.</param>
    /// <param name="functionContext">
    /// The state to apply to every listed occurrence; only the members named in
    /// <see cref="JobExecutionState.PropertiesToUpdate"/> are persisted.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the batch.</param>
    /// <returns>
    /// The subset of <paramref name="timeJobIds"/> that passed the fence and were stamped, in no guaranteed order.
    /// </returns>
    /// <remarks>
    /// The cron mirror of <see cref="UpdateTimeJobsWithUnifiedContextAsync"/>, including the strict claim-to-start
    /// recheck: an <c>InProgress</c> transition additionally requires the occurrence to still be <c>Queued</c>.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<Guid[]> UpdateCronJobOccurrencesWithUnifiedContextAsync(
        Guid[] timeJobIds,
        JobExecutionState functionContext,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Recovers the cron occurrences held by a node that coordination has declared dead, applying each row's
    /// <c>OnNodeDeath</c> policy. The cron mirror of <see cref="ReleaseDeadNodeTimeJobResourcesAsync"/>.
    /// </summary>
    /// <param name="instanceIdentifier">
    /// The dead node's <c>node@incarnation</c> owner identity. Only occurrences owned by exactly this identity are
    /// considered.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the sweep.</param>
    /// <returns>The number of occurrences transitioned; <c>0</c> when the dead node held nothing recoverable.</returns>
    /// <remarks>
    /// <c>Idle</c> and <c>Queued</c> occurrences are reclaimed immediately; <c>InProgress</c> occurrences only
    /// once their lease has also lapsed, leaving a still-leased running occurrence to
    /// <see cref="ReclaimStalledCronJobOccurrencesAsync"/>.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> ReleaseDeadNodeOccurrenceResourcesAsync(
        string instanceIdentifier,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Slides the running cron occurrence's lease forward (<c>LockedUntil = now + LeaseDuration</c>), fenced on
    /// current ownership + non-terminal status (#316/KTD3). Returns <c>1</c> when renewed, <c>0</c> when the
    /// lease was lost — the caller treats <c>0</c> as cancel-on-loss — or a <b>negative</b> value when coordination
    /// membership is not currently established (#461), treated as "skip this renewal tick", not loss.
    /// </summary>
    /// <param name="occurrenceId">Identifier of the occurrence whose lease is being renewed.</param>
    /// <param name="cancellationToken">Token that aborts the renewal.</param>
    /// <returns>
    /// <c>1</c> renewed, <c>0</c> lease lost, negative membership-unavailable — see
    /// <see cref="RenewTimeJobLeaseAsync"/> for why collapsing the negative case into <c>0</c> is a correctness bug.
    /// </returns>
    /// <remarks>
    /// As with time jobs, renewal slides a <b>running</b> lease only: an <c>Idle</c> or <c>Queued</c> occurrence
    /// must return <c>0</c> so cancel-on-loss is not suppressed.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> RenewCronJobOccurrenceLeaseAsync(Guid occurrenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims cron occurrences stuck <c>InProgress</c> whose lease lapsed (#316/U3) — the cron mirror of
    /// <see cref="ReclaimStalledTimeJobsAsync"/>, applying the same per-<c>OnNodeDeath</c> transitions. Returns the
    /// affected count.
    /// </summary>
    /// <param name="cancellationToken">Token that aborts the sweep.</param>
    /// <returns>The number of occurrences transitioned by this sweep; <c>0</c> when nothing had stalled.</returns>
    /// <remarks>
    /// Not owner-scoped: the trigger is a lapsed lease on any node, not a declared node death.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> ReclaimStalledCronJobOccurrencesAsync(CancellationToken cancellationToken = default);
    #endregion

    #region Time_Ticker_Shared_Methods

    /// <summary>
    /// Loads a single time job by identifier, with its child jobs attached, for management and dashboard reads.
    /// </summary>
    /// <param name="id">Identifier of the time job.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The job with its children, or <see langword="null"/> when no such job exists.</returns>
    /// <remarks>
    /// A read-only projection: it takes no ownership and does not participate in the claim or lease protocol, so
    /// the returned entity must never be written back through this SPI.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<TTimeJob?> GetTimeJobByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries root time jobs — those with no parent — with their child hierarchy attached.
    /// </summary>
    /// <param name="predicate">
    /// Optional filter applied before the root restriction; <see langword="null"/> returns all roots. Because the
    /// filter runs before roots are selected, a predicate that matches only child rows yields nothing.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>Matching root jobs ordered by execution time, most recent first; empty when nothing matches.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<TTimeJob[]> GetTimeJobsAsync(
        Expression<Func<TTimeJob, bool>>? predicate,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Page of root time jobs with their child hierarchy attached, for dashboard listings.
    /// </summary>
    /// <param name="predicate">Optional filter applied before the root restriction; <see langword="null"/> returns all roots.</param>
    /// <param name="pageNumber">The <b>1-based</b> page to return.</param>
    /// <param name="pageSize">Maximum number of root jobs on the page.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>
    /// The requested page ordered by execution time, most recent first, with a total count that reflects matching
    /// <b>root</b> jobs only — child rows are never counted or paged independently.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<PaginationResult<TTimeJob>> GetTimeJobsPaginatedAsync(
        Expression<Func<TTimeJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Inserts new time jobs together with their child chains. This is the raw storage write behind the manager's
    /// enqueue path; it performs no validation and no coordination.
    /// </summary>
    /// <param name="jobs">The root jobs to insert; nested children are inserted with their parent link set.</param>
    /// <param name="cancellationToken">Token that aborts the write.</param>
    /// <returns>
    /// The number of rows written, which counts inserted <b>children as well as roots</b> and is therefore usually
    /// larger than <c>jobs.Length</c> for job chains.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> AddTimeJobsAsync(TTimeJob[] jobs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites existing time jobs and their child chains wholesale.
    /// </summary>
    /// <param name="jobs">The jobs to write back, each identified by its <c>Id</c>.</param>
    /// <param name="cancellationToken">Token that aborts the write.</param>
    /// <returns>The number of rows written, counting updated children as well as roots.</returns>
    /// <remarks>
    /// This is a full-row write with <b>no ownership or completion fence</b> — it is the management/dashboard edit
    /// path, not the execution path. Writing a running job through it can clobber the executing node's state; the
    /// scheduler must use <see cref="UpdateTimeJobAsync"/> or
    /// <see cref="UpdateTimeJobsWithUnifiedContextAsync"/> instead.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> UpdateTimeJobsAsync(TTimeJob[] jobs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes time jobs and cascades to their child chains.
    /// </summary>
    /// <param name="jobIds">Identifiers of the root jobs to delete.</param>
    /// <param name="cancellationToken">Token that aborts the delete.</param>
    /// <returns>The number of rows deleted, including cascaded children.</returns>
    /// <remarks>
    /// Unconditional: a running job is deleted out from under its executing node, which surfaces there as lease
    /// loss on the next renewal.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> RemoveTimeJobsAsync(Guid[] jobIds, CancellationToken cancellationToken = default);
    #endregion

    #region Cron_Ticker_Shared_Methods

    /// <summary>
    /// Loads a single cron definition by identifier, for management and dashboard reads.
    /// </summary>
    /// <param name="id">Identifier of the cron definition.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The cron definition, or <see langword="null"/> when no such definition exists.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<TCronJob?> GetCronJobByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries cron definitions.
    /// </summary>
    /// <param name="predicate">Optional filter; <see langword="null"/> returns every definition.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>Matching definitions ordered by creation time, newest first; empty when nothing matches.</returns>
    /// <remarks>
    /// Unlike <see cref="GetAllCronJobExpressionsAsync"/> this is an uncached read intended for management
    /// surfaces; do not call it on the scheduler tick path.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<TCronJob[]> GetCronJobsAsync(
        Expression<Func<TCronJob, bool>>? predicate,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Page of cron definitions, for dashboard listings.
    /// </summary>
    /// <param name="predicate">Optional filter; <see langword="null"/> returns every definition.</param>
    /// <param name="pageNumber">The <b>1-based</b> page to return.</param>
    /// <param name="pageSize">Maximum number of definitions on the page.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The requested page ordered by creation time, newest first, with the total matching count.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<PaginationResult<TCronJob>> GetCronJobsPaginatedAsync(
        Expression<Func<TCronJob, bool>>? predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Inserts application-defined cron definitions — the runtime counterpart to the code-declared definitions
    /// seeded by <see cref="MigrateDefinedCronJobsAsync"/>.
    /// </summary>
    /// <param name="jobs">The cron definitions to insert.</param>
    /// <param name="cancellationToken">Token that aborts the write.</param>
    /// <returns>The number of definitions inserted; definitions whose identifier already exists are skipped.</returns>
    /// <remarks>
    /// Implementations that cache <see cref="GetAllCronJobExpressionsAsync"/> must invalidate that entry here,
    /// otherwise the new schedule does not take effect until the cached entry expires.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> InsertCronJobsAsync(TCronJob[] jobs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites existing cron definitions wholesale — the management edit path for changing an expression,
    /// payload, or retry policy.
    /// </summary>
    /// <param name="cronJob">The definitions to write back, each identified by its <c>Id</c>.</param>
    /// <param name="cancellationToken">Token that aborts the write.</param>
    /// <returns>The number of definitions updated; unknown identifiers are skipped.</returns>
    /// <remarks>
    /// Implementations that cache <see cref="GetAllCronJobExpressionsAsync"/> must invalidate that entry here.
    /// Already-materialized occurrences are not retroactively rescheduled; a changed expression takes effect from
    /// the next occurrence the scheduler generates.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> UpdateCronJobsAsync(TCronJob[] cronJob, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes cron definitions.
    /// </summary>
    /// <param name="cronJobIds">Identifiers of the definitions to delete.</param>
    /// <param name="cancellationToken">Token that aborts the delete.</param>
    /// <returns>The number of definitions deleted.</returns>
    /// <remarks>
    /// Implementations that cache <see cref="GetAllCronJobExpressionsAsync"/> must invalidate that entry here.
    /// Whether already-materialized occurrences are removed alongside the definition is governed by the store's
    /// cascade configuration, not by this member.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> RemoveCronJobsAsync(Guid[] cronJobIds, CancellationToken cancellationToken = default);
    #endregion

    #region Cron_TickerOccurrence_Shared_Methods

    /// <summary>
    /// Queries cron occurrences with their owning definition attached, for management and dashboard reads.
    /// </summary>
    /// <param name="predicate">Optional filter; <see langword="null"/> returns every occurrence.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>
    /// Matching occurrences, newest first; empty when nothing matches. The exact ordering key is provider-defined
    /// — the relational provider orders by execution time and the in-memory provider by creation time — so callers
    /// that depend on a precise order must sort the result themselves.
    /// </returns>
    /// <remarks>
    /// Unbounded: this materializes every matching occurrence. Prefer
    /// <see cref="GetAllCronJobOccurrencesPaginatedAsync"/> for user-facing listings and
    /// <see cref="GetCronOccurrenceGraphStatusCountsAsync"/> for aggregates.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<CronJobOccurrenceEntity<TCronJob>[]> GetAllCronJobOccurrencesAsync(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>>? predicate,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns storage-reduced status counts for the cron-occurrence dashboard graph, plus zero-count boundary
    /// entries that identify the exact inclusive date range. The default implementation preserves compatibility
    /// for third-party providers by projecting through <see cref="GetAllCronJobOccurrencesAsync"/>; providers
    /// should override it to project distinct dates and aggregate counts in storage.
    /// </summary>
    /// <param name="cronJobId">Identifier of the cron job whose occurrence history is projected.</param>
    /// <param name="today">Current UTC calendar date used to balance the graph around today.</param>
    /// <param name="cancellationToken">Token that can abort the provider query.</param>
    /// <returns>
    /// One entry per observed (date, status) pair inside the selected range, plus two zero-count boundary entries
    /// pinning the range's first and last day so the dashboard can render a stable axis. Counts are per calendar
    /// day in UTC.
    /// </returns>
    /// <remarks>
    /// This is the reference case for the additive-evolution policy on this interface: it is a default interface
    /// method precisely so that adding it did not break existing implementers. The default is correct but loads
    /// every occurrence for the cron job into memory to aggregate it, so any provider that can express a
    /// <c>GROUP BY</c> in storage should override it.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    async Task<CronOccurrenceStatusCount[]> GetCronOccurrenceGraphStatusCountsAsync(
        Guid cronJobId,
        DateTime today,
        CancellationToken cancellationToken = default
    )
    {
        var occurrences = await GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronJobId, cancellationToken)
            .ConfigureAwait(false);
        var range = CronOccurrenceGraphRangeSelector.Select(occurrences.Select(x => x.ExecutionTime), today);
        var counts = occurrences
            .Where(x => x.ExecutionTime.Date >= range.StartDate && x.ExecutionTime.Date <= range.EndDate)
            .GroupBy(x => new { x.ExecutionTime.Date, x.Status })
            .Select(group => new CronOccurrenceStatusCount
            {
                Date = group.Key.Date,
                Status = group.Key.Status,
                Count = group.Count(),
            });

        return CronOccurrenceGraphRangeSelector.AddRangeBoundaries(counts, range);
    }

    /// <summary>
    /// Page of cron occurrences with their owning definition attached, for dashboard listings.
    /// </summary>
    /// <param name="predicate">
    /// Filter selecting the occurrences to page — typically "belongs to this cron definition".
    /// </param>
    /// <param name="pageNumber">The <b>1-based</b> page to return.</param>
    /// <param name="pageSize">Maximum number of occurrences on the page.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>
    /// The requested page, newest first, with the total matching count. As with
    /// <see cref="GetAllCronJobOccurrencesAsync"/> the exact ordering key is provider-defined.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<PaginationResult<CronJobOccurrenceEntity<TCronJob>>> GetAllCronJobOccurrencesPaginatedAsync(
        Expression<Func<CronJobOccurrenceEntity<TCronJob>, bool>> predicate,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Inserts occurrence rows directly, bypassing the scheduler's materialization path. Used for on-demand
    /// "run this cron now" requests and by management tooling.
    /// </summary>
    /// <param name="cronJobOccurrences">The occurrence rows to insert.</param>
    /// <param name="cancellationToken">Token that aborts the write.</param>
    /// <returns>The number of occurrences inserted; rows whose identifier already exists are skipped.</returns>
    /// <remarks>
    /// Unlike <see cref="QueueCronJobOccurrencesAsync"/> this takes no ownership and stamps no lease — the rows
    /// land unclaimed and are picked up by a later scheduler sweep.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> InsertCronJobOccurrencesAsync(
        CronJobOccurrenceEntity<TCronJob>[] cronJobOccurrences,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes cron occurrence rows — the history-pruning path for the dashboard.
    /// </summary>
    /// <param name="cronJobOccurrences">Identifiers of the occurrences to delete.</param>
    /// <param name="cancellationToken">Token that aborts the delete.</param>
    /// <returns>The number of occurrences deleted.</returns>
    /// <remarks>
    /// Unconditional: a running occurrence is deleted out from under its executing node, which surfaces there as
    /// lease loss on the next renewal.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> RemoveCronJobOccurrencesAsync(Guid[] cronJobOccurrences, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims specific cron occurrences for immediate out-of-band execution — the on-demand "run this now" path —
    /// taking them straight to <c>InProgress</c> with a fresh lease. The cron mirror of
    /// <see cref="AcquireImmediateTimeJobsAsync"/>.
    /// </summary>
    /// <param name="occurrenceIds">
    /// The occurrences to claim. An empty array returns an empty result without touching storage.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the claim.</param>
    /// <returns>
    /// Only the occurrences this node actually claimed, with their cron definition attached so the function name
    /// and payload are resolvable. An empty result means the request was fully lost and nothing should be executed.
    /// </returns>
    /// <remarks>
    /// The relational provider returns empty when coordination membership is not established, because it has no
    /// owner identity to stamp.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<CronJobOccurrenceEntity<TCronJob>[]> AcquireImmediateCronOccurrencesAsync(
        Guid[] occurrenceIds,
        CancellationToken cancellationToken = default
    );
    #endregion
}
