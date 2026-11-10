// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Jobs;

// The reads a dashboard needs: one keyset list over time jobs and cron occurrences, capped counts, failure groups,
// Operation scopes with their compare-and-set writes, Overview figures, and per-function stats. A provider implements
// the per-table members; the members that span both tables are default methods built on them.
public partial interface IJobPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    #region Dashboard reads

    /// <summary>
    /// Returns at most <paramref name="maxCount"/> runs of one table that <paramref name="query"/> matches, in
    /// <see cref="JobRunQuery.SortBy"/> order and then identifier order (both descending when
    /// <see cref="JobRunQuery.SortDirection"/> is descending), starting after <paramref name="after"/>.
    /// </summary>
    /// <param name="kind">The table to read.</param>
    /// <param name="query">The runs to read; the kind filter is the caller's to apply.</param>
    /// <param name="after">The keyset position of the last run an earlier call returned, exclusive, or <see langword="null"/>.</param>
    /// <param name="maxCount">The most runs to return.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The runs, in keyset order.</returns>
    /// <remarks>
    /// The sort key of a run without an execution time is <see cref="JobRunPosition.Unscheduled"/>. The keyset
    /// predicate compares the sort key exactly as the order does, so a table pages without repeat or gap. The member
    /// <see cref="ListJobRunsAsync"/> is built on. The default throws.
    /// </remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<IReadOnlyList<JobRunSummary>> ReadJobRunRowsAsync(
        JobRunKind kind,
        JobRunQuery query,
        JobRunPosition? after,
        int maxCount,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>
    /// Returns one page of the runs <paramref name="query"/> matches, time jobs and cron occurrences together, in the
    /// query's order, and the cursor of the next page.
    /// </summary>
    /// <param name="query">The runs to read.</param>
    /// <param name="after">
    /// The cursor of the page to read, or <see langword="null"/> for the first page. It must
    /// <see cref="JobRunCursor.Fits"/> <paramref name="query"/>.
    /// </param>
    /// <param name="pageSize">The most runs to return.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The page.</returns>
    /// <remarks>
    /// Each table pages forward on its own keyset and the page takes a prefix of each, merged by the sort key, so a
    /// run never repeats or goes missing at a page boundary. Runs of the two kinds with one sort key list time jobs
    /// first. Default method built on <see cref="ReadJobRunRowsAsync"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageSize"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="after"/> was read with another sort, direction, filter, or search than <paramref name="query"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    async Task<JobRunPage> ListJobRunsAsync(
        JobRunQuery query,
        JobRunCursor? after,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsPositive(pageSize);

        if (after?.Fits(query) == false)
        {
            throw new ArgumentException(
                "The cursor was read with another sort, direction, filter, or search than this query.",
                nameof(after)
            );
        }

        var cursor = after ?? JobRunCursor.Start(query);
        var timeJobs = query.IncludesTimeJobs
            ? await ReadJobRunRowsAsync(JobRunKind.TimeJob, query, cursor.TimeJobs, pageSize, cancellationToken)
                .ConfigureAwait(false)
            : [];
        var occurrences = query.IncludesCronJobOccurrences
            ? await ReadJobRunRowsAsync(
                    JobRunKind.CronJobOccurrence,
                    query,
                    cursor.CronJobOccurrences,
                    pageSize,
                    cancellationToken
                )
                .ConfigureAwait(false)
            : [];

        // A two-way merge that never reorders a table's rows: a store's text collation can disagree with any .NET
        // comparer, so the comparer only interleaves the tables, and each table's position moves to the last row
        // taken from it. Ties go to the time job, in either direction.
        var page = new List<JobRunSummary>(Math.Min(pageSize, timeJobs.Count + occurrences.Count));
        var next = cursor;
        var nextTimeJob = 0;
        var nextOccurrence = 0;

        while (page.Count < pageSize && (nextTimeJob < timeJobs.Count || nextOccurrence < occurrences.Count))
        {
            JobRunSummary run;

            if (nextOccurrence >= occurrences.Count)
            {
                run = timeJobs[nextTimeJob++];
            }
            else if (nextTimeJob >= timeJobs.Count)
            {
                run = occurrences[nextOccurrence++];
            }
            else
            {
                var order = JobRunPosition
                    .Of(timeJobs[nextTimeJob])
                    .CompareKey(query.SortBy, JobRunPosition.Of(occurrences[nextOccurrence]));

                run =
                    (query.Descending ? order >= 0 : order <= 0)
                        ? timeJobs[nextTimeJob++]
                        : occurrences[nextOccurrence++];
            }

            page.Add(run);
            next = next.WithPosition(run.Kind, JobRunPosition.Of(run));
        }

        var more =
            timeJobs.Count == pageSize
            || occurrences.Count == pageSize
            || nextTimeJob < timeJobs.Count
            || nextOccurrence < occurrences.Count;

        return new JobRunPage { Runs = page, Next = more ? next : null };
    }

    /// <summary>
    /// Counts the runs of one table that <paramref name="query"/> matches, stopping at <paramref name="limit"/>.
    /// </summary>
    /// <param name="kind">The table to count.</param>
    /// <param name="query">The runs to count; the kind filter is the caller's to apply.</param>
    /// <param name="limit">The most runs to count.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The count, at most <paramref name="limit"/>.</returns>
    /// <remarks>The member <see cref="CountJobRunsAsync"/> is built on. The default throws.</remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> CountJobRunRowsAsync(
        JobRunKind kind,
        JobRunQuery query,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>
    /// Counts the runs <paramref name="query"/> matches, stopping at <paramref name="cap"/>: a larger count reports the
    /// cap with <see cref="JobRunCount.Capped"/> set.
    /// </summary>
    /// <param name="query">The runs to count.</param>
    /// <param name="cap">The cap; the dashboard counts to <see cref="JobRunCount.DefaultCap"/>.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The capped count.</returns>
    /// <remarks>Default method built on <see cref="CountJobRunRowsAsync"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is not positive.</exception>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    async Task<JobRunCount> CountJobRunsAsync(
        JobRunQuery query,
        int cap = JobRunCount.DefaultCap,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsPositive(cap);

        long matched = 0;

        foreach (var kind in _RunKinds(query))
        {
            // One row past the cap proves the count is capped; the second table only needs what the first left.
            matched += await CountJobRunRowsAsync(kind, query, (int)(cap + 1 - matched), cancellationToken)
                .ConfigureAwait(false);

            if (matched > cap)
            {
                break;
            }
        }

        return JobRunCount.Of(matched, cap);
    }

    /// <summary>Returns the run, a time job or a cron occurrence, with the given identifier.</summary>
    /// <param name="id">The run's identifier.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The run, or <see langword="null"/> when no run has the identifier.</returns>
    /// <remarks>The default throws.</remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<JobRunSummary?> GetJobRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>Returns the cron occurrence with the given identifier.</summary>
    /// <param name="id">The occurrence's identifier.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The occurrence, or <see langword="null"/> when none has the identifier.</returns>
    /// <remarks>The default throws.</remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<CronJobOccurrenceEntity<TCronJob>?> GetCronJobOccurrenceByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>
    /// Returns the actionable failures of one table (see <see cref="JobRequeueRules"/>) grouped by function and failure
    /// fingerprint.
    /// </summary>
    /// <param name="kind">The table to read.</param>
    /// <param name="recentSince">The bound <see cref="JobFailureGroup.RecentCount"/> counts from.</param>
    /// <param name="function">The one function to group, or <see langword="null"/> for every function.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>Every group, in no particular order.</returns>
    /// <remarks>
    /// A run's failure time is when its last execution ended. The member <see cref="GetJobFailureGroupsAsync"/> is
    /// built on. The default throws.
    /// </remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<IReadOnlyList<JobFailureGroup>> ReadJobFailureGroupRowsAsync(
        JobRunKind kind,
        DateTimeOffset recentSince,
        string? function,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>
    /// Returns the actionable failures (see <see cref="JobRequeueRules"/>) of time jobs and cron occurrences grouped by
    /// function and failure fingerprint, latest failure first.
    /// </summary>
    /// <param name="recentSince">The bound <see cref="JobFailureGroup.RecentCount"/> counts from.</param>
    /// <param name="function">The one function to group, or <see langword="null"/> for every function.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>Every group.</returns>
    /// <remarks>
    /// A failed time job stays in its group until it is requeued or deleted. A failed cron occurrence leaves its group
    /// once a later occurrence of the same cron job succeeds. Chain members and superseded keyed generations, which a
    /// requeue refuses, are never counted. Default method built on <see cref="ReadJobFailureGroupRowsAsync"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    async Task<IReadOnlyList<JobFailureGroup>> GetJobFailureGroupsAsync(
        DateTimeOffset recentSince,
        string? function = null,
        CancellationToken cancellationToken = default
    )
    {
        var groups = new Dictionary<(string Function, string? Fingerprint), JobFailureGroup>();

        foreach (var kind in (JobRunKind[])[JobRunKind.TimeJob, JobRunKind.CronJobOccurrence])
        {
            var rows = await ReadJobFailureGroupRowsAsync(kind, recentSince, function, cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in rows)
            {
                var key = (row.Function, row.FailureFingerprint);

                groups[key] = groups.TryGetValue(key, out var merged)
                    ? merged with
                    {
                        Count = merged.Count + row.Count,
                        RecentCount = merged.RecentCount + row.RecentCount,
                        FirstFailedAt =
                            row.FirstFailedAt < merged.FirstFailedAt ? row.FirstFailedAt : merged.FirstFailedAt,
                        LastFailedAt = row.LastFailedAt > merged.LastFailedAt ? row.LastFailedAt : merged.LastFailedAt,
                    }
                    : row;
            }
        }

        return
        [
            .. groups
                .Values.OrderByDescending(static group => group.LastFailedAt)
                .ThenBy(static group => group.Function, StringComparer.Ordinal)
                .ThenBy(static group => group.FailureFingerprint, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Returns at most <paramref name="maxCount"/> runs of one table in an Operation's frozen scope, in identifier
    /// order after <paramref name="afterId"/>, each with the change marker its write expects.
    /// </summary>
    /// <param name="kind">The table to read.</param>
    /// <param name="scope">The Operation's scope.</param>
    /// <param name="afterId">The last identifier an earlier call returned, exclusive, or <see langword="null"/>.</param>
    /// <param name="maxCount">The most runs to return.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The runs, in the store's identifier order.</returns>
    /// <remarks>
    /// <para>
    /// A run is in scope when <see cref="JobOperationScope.Query"/> matches it and the command applies to it now:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// Requeue: its marker is at or before <see cref="JobOperationScope.HighWaterMark"/> and
    /// <see cref="JobRequeueRules"/> lets it be requeued.
    /// </description></item>
    /// <item><description>
    /// Cancel: a time job created at or before <see cref="JobOperationScope.ConfirmedAt"/>, idle, queued, or running,
    /// with no cancellation requested yet. Cron occurrences take no cancel request, so their table is never in scope.
    /// </description></item>
    /// <item><description>
    /// Delete: its marker is at or before <see cref="JobOperationScope.HighWaterMark"/>, it is not running, and it is
    /// not a keyed time job, whose generations are retained.
    /// </description></item>
    /// </list>
    /// <para>The member <see cref="ReadJobOperationScopeAsync"/> is built on. The default throws.</para>
    /// </remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<IReadOnlyList<JobOperationTarget>> ReadJobOperationTargetsAsync(
        JobRunKind kind,
        JobOperationScope scope,
        Guid? afterId,
        int maxCount,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>
    /// Counts the runs of one table in an Operation's frozen scope, stopping at <paramref name="limit"/>.
    /// </summary>
    /// <param name="kind">The table to count.</param>
    /// <param name="scope">The Operation's scope; see <see cref="ReadJobOperationTargetsAsync"/>.</param>
    /// <param name="limit">The most runs to count.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The count, at most <paramref name="limit"/>.</returns>
    /// <remarks>The member <see cref="CountJobOperationScopeAsync"/> is built on. The default throws.</remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<int> CountJobOperationTargetsAsync(
        JobRunKind kind,
        JobOperationScope scope,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>
    /// Counts the runs in an Operation's frozen scope, stopping at <paramref name="cap"/>, so the confirm step can
    /// show the reach.
    /// </summary>
    /// <param name="scope">The Operation's scope.</param>
    /// <param name="cap">The cap.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The capped count.</returns>
    /// <remarks>Default method built on <see cref="CountJobOperationTargetsAsync"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is not positive.</exception>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    async Task<JobRunCount> CountJobOperationScopeAsync(
        JobOperationScope scope,
        int cap = JobRunCount.DefaultCap,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(scope);
        Argument.IsPositive(cap);

        long matched = 0;

        foreach (var kind in _RunKinds(scope.Query))
        {
            matched += await CountJobOperationTargetsAsync(kind, scope, (int)(cap + 1 - matched), cancellationToken)
                .ConfigureAwait(false);

            if (matched > cap)
            {
                break;
            }
        }

        return JobRunCount.Of(matched, cap);
    }

    /// <summary>
    /// Returns the next chunk of an Operation's frozen scope: time jobs first, then cron occurrences, each in
    /// identifier order.
    /// </summary>
    /// <param name="scope">The Operation's scope.</param>
    /// <param name="after">The cursor an earlier chunk returned, or <see langword="null"/> for the first chunk.</param>
    /// <param name="chunkSize">The most runs to return.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The chunk.</returns>
    /// <remarks>
    /// Apply requeue and delete to each run with <see cref="RequeueTimeJobIfUnchangedAsync"/>,
    /// <see cref="RequeueCronJobOccurrenceIfUnchangedAsync"/>, <see cref="RemoveTimeJobIfUnchangedAsync"/>, and
    /// <see cref="RemoveCronJobOccurrenceIfUnchangedAsync"/>, passing the marker the chunk returned. Cancel goes through
    /// <see cref="RequestTimeJobCancellationAsync"/>, which already refuses a repeated request. Default method built on
    /// <see cref="ReadJobOperationTargetsAsync"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chunkSize"/> is not positive.</exception>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    async Task<JobOperationChunk> ReadJobOperationScopeAsync(
        JobOperationScope scope,
        JobOperationCursor? after,
        int chunkSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(scope);
        Argument.IsPositive(chunkSize);

        var kinds = _RunKinds(scope.Query);
        var start = after is { } cursor ? Array.IndexOf(kinds, cursor.Kind) : 0;

        if (start < 0)
        {
            return new JobOperationChunk { Targets = [] };
        }

        var targets = new List<JobOperationTarget>(chunkSize);

        for (var index = start; index < kinds.Length && targets.Count < chunkSize; index++)
        {
            var kind = kinds[index];
            var afterId = after is { } position && position.Kind == kind ? position.AfterId : (Guid?)null;
            var rows = await ReadJobOperationTargetsAsync(
                    kind,
                    scope,
                    afterId,
                    chunkSize - targets.Count,
                    cancellationToken
                )
                .ConfigureAwait(false);

            targets.AddRange(rows);
        }

        // A full chunk may be the scope's last runs; the next read then returns an empty chunk with no cursor.
        return new JobOperationChunk
        {
            Targets = targets,
            Next = targets.Count == chunkSize ? new JobOperationCursor(targets[^1].Kind, targets[^1].Id) : null,
        };
    }

    /// <summary>
    /// Requeues a time job as <see cref="RequeueTimeJobAsync"/> does, only while its change marker still equals
    /// <paramref name="expectedChangedAt"/>.
    /// </summary>
    /// <param name="jobId">Identifier of the time job to requeue.</param>
    /// <param name="expectedChangedAt">The marker an Operation's scope read returned for the row.</param>
    /// <param name="traceParent">W3C <c>traceparent</c> of the requeue request, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token that aborts the requeue itself.</param>
    /// <returns>
    /// <see cref="JobRequeueOutcome.Requeued"/> when this call moved the row; <see cref="JobRequeueOutcome.Conflict"/>
    /// when the row changed since the scope read and the rules would still accept it; otherwise the refusal reason.
    /// </returns>
    /// <remarks>
    /// The marker check is part of the one conditional write, so a run requeued and failed again since the scope read
    /// is not requeued a second time by one Operation. The default throws.
    /// </remarks>
    /// <exception cref="NotSupportedException">The provider does not support Operation writes.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<JobRequeueOutcome> RequeueTimeJobIfUnchangedAsync(
        Guid jobId,
        DateTime expectedChangedAt,
        string? traceParent,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs Operation writes.");
    }

    /// <summary>
    /// Requeues a cron occurrence as <see cref="RequeueCronJobOccurrenceAsync"/> does, only while its change marker
    /// still equals <paramref name="expectedChangedAt"/>.
    /// </summary>
    /// <param name="occurrenceId">Identifier of the occurrence to requeue.</param>
    /// <param name="expectedChangedAt">The marker an Operation's scope read returned for the row.</param>
    /// <param name="traceParent">W3C <c>traceparent</c> of the requeue request, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token that aborts the requeue itself.</param>
    /// <returns>As <see cref="RequeueTimeJobIfUnchangedAsync"/> describes.</returns>
    /// <remarks>The default throws.</remarks>
    /// <exception cref="NotSupportedException">The provider does not support Operation writes.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<JobRequeueOutcome> RequeueCronJobOccurrenceIfUnchangedAsync(
        Guid occurrenceId,
        DateTime expectedChangedAt,
        string? traceParent,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs Operation writes.");
    }

    /// <summary>
    /// Deletes a time job and its chain subtree, as <see cref="RemoveTimeJobsAsync"/> does, only while the job is not
    /// running and its change marker still equals <paramref name="expectedChangedAt"/>.
    /// </summary>
    /// <param name="jobId">Identifier of the time job to delete.</param>
    /// <param name="expectedChangedAt">The marker an Operation's scope read returned for the row.</param>
    /// <param name="cancellationToken">Token that aborts the delete.</param>
    /// <returns><see langword="true"/> when this call deleted the job.</returns>
    /// <remarks>The check and the delete commit together. The default throws.</remarks>
    /// <exception cref="InvalidOperationException">The job is keyed; keyed generations are retained.</exception>
    /// <exception cref="NotSupportedException">The provider does not support Operation writes.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<bool> RemoveTimeJobIfUnchangedAsync(
        Guid jobId,
        DateTime expectedChangedAt,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs Operation writes.");
    }

    /// <summary>
    /// Deletes a cron occurrence only while it is not running and its change marker still equals
    /// <paramref name="expectedChangedAt"/>.
    /// </summary>
    /// <param name="occurrenceId">Identifier of the occurrence to delete.</param>
    /// <param name="expectedChangedAt">The marker an Operation's scope read returned for the row.</param>
    /// <param name="cancellationToken">Token that aborts the delete.</param>
    /// <returns><see langword="true"/> when this call deleted the occurrence.</returns>
    /// <remarks>One conditional delete. The default throws.</remarks>
    /// <exception cref="NotSupportedException">The provider does not support Operation writes.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<bool> RemoveCronJobOccurrenceIfUnchangedAsync(
        Guid occurrenceId,
        DateTime expectedChangedAt,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs Operation writes.");
    }

    /// <summary>Counts one table's runs that finished or started late in the window [<paramref name="from"/>, <paramref name="to"/>).</summary>
    /// <param name="kind">The table to count.</param>
    /// <param name="from">The window's inclusive start (UTC).</param>
    /// <param name="to">The window's exclusive end (UTC).</param>
    /// <param name="lateAfter">How long after its execution time a run's first attempt may start before it is late.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The window's figures.</returns>
    /// <remarks>
    /// A run counts as succeeded (<see cref="JobRequeueRules.IsSuccess"/>) or failed when its last execution ended in
    /// the window. A run counts as late when its earliest attempt started in the window more than
    /// <paramref name="lateAfter"/> after its execution time; a store without attempt history reports none late. The
    /// member <see cref="GetJobOverviewFiguresAsync"/> is built on. The default throws.
    /// </remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<JobRunWindowFigures> CountJobRunWindowAsync(
        JobRunKind kind,
        DateTime from,
        DateTime to,
        TimeSpan lateAfter,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>
    /// Returns the Overview figures: the runs that succeeded, failed, and started late in the 24 hours before
    /// <paramref name="now"/>, and in the 24 hours before those for comparison.
    /// </summary>
    /// <param name="now">The end of the current window (UTC).</param>
    /// <param name="lateAfter">How long after it was due a run may start before it is late, one poll interval.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The figures.</returns>
    /// <remarks>Default method built on <see cref="CountJobRunWindowAsync"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lateAfter"/> is negative.</exception>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    async Task<JobOverviewFigures> GetJobOverviewFiguresAsync(
        DateTime now,
        TimeSpan lateAfter,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositiveOrZero(lateAfter);

        var end = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        var start = end - JobOverviewFigures.Window;
        var current = await _CountWindowAsync(start, end, lateAfter, cancellationToken).ConfigureAwait(false);
        var previous = await _CountWindowAsync(start - JobOverviewFigures.Window, start, lateAfter, cancellationToken)
            .ConfigureAwait(false);

        return new JobOverviewFigures(current, previous);
    }

    /// <summary>Returns per-function stats of one table.</summary>
    /// <param name="kind">The table to read.</param>
    /// <param name="since">The bound the succeeded and failed counts start from.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>One entry per function with at least one run in the table, in no particular order.</returns>
    /// <remarks>The member <see cref="GetJobFunctionStatsAsync"/> is built on. The default throws.</remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<IReadOnlyList<JobFunctionStats>> ReadJobFunctionStatsRowsAsync(
        JobRunKind kind,
        DateTimeOffset since,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>
    /// Returns, for each function with at least one stored run, how many runs succeeded and failed since
    /// <paramref name="since"/>, how many are running or waiting now, and when the latest ended.
    /// </summary>
    /// <param name="since">The bound the succeeded and failed counts start from.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>One entry per function, ordered by function.</returns>
    /// <remarks>Default method built on <see cref="ReadJobFunctionStatsRowsAsync"/>.</remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    async Task<IReadOnlyList<JobFunctionStats>> GetJobFunctionStatsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default
    )
    {
        var stats = new Dictionary<string, JobFunctionStats>(StringComparer.Ordinal);

        foreach (var kind in (JobRunKind[])[JobRunKind.TimeJob, JobRunKind.CronJobOccurrence])
        {
            foreach (
                var row in await ReadJobFunctionStatsRowsAsync(kind, since, cancellationToken).ConfigureAwait(false)
            )
            {
                stats[row.Function] = stats.TryGetValue(row.Function, out var merged)
                    ? merged with
                    {
                        Succeeded = merged.Succeeded + row.Succeeded,
                        Failed = merged.Failed + row.Failed,
                        Running = merged.Running + row.Running,
                        Waiting = merged.Waiting + row.Waiting,
                        LastExecutedAt =
                            row.LastExecutedAt > merged.LastExecutedAt || merged.LastExecutedAt is null
                                ? row.LastExecutedAt
                                : merged.LastExecutedAt,
                    }
                    : row;
            }
        }

        return [.. stats.Values.OrderBy(static row => row.Function, StringComparer.Ordinal)];
    }

    private async Task<JobRunWindowFigures> _CountWindowAsync(
        DateTime from,
        DateTime to,
        TimeSpan lateAfter,
        CancellationToken cancellationToken
    )
    {
        var timeJobs = await CountJobRunWindowAsync(JobRunKind.TimeJob, from, to, lateAfter, cancellationToken)
            .ConfigureAwait(false);
        var occurrences = await CountJobRunWindowAsync(
                JobRunKind.CronJobOccurrence,
                from,
                to,
                lateAfter,
                cancellationToken
            )
            .ConfigureAwait(false);

        return new JobRunWindowFigures(
            timeJobs.Succeeded + occurrences.Succeeded,
            timeJobs.Failed + occurrences.Failed,
            timeJobs.Late + occurrences.Late
        );
    }

    private static JobRunKind[] _RunKinds(JobRunQuery query)
    {
        return (query.IncludesTimeJobs, query.IncludesCronJobOccurrences) switch
        {
            (true, true) => [JobRunKind.TimeJob, JobRunKind.CronJobOccurrence],
            (true, false) => [JobRunKind.TimeJob],
            (false, true) => [JobRunKind.CronJobOccurrence],
            _ => [],
        };
    }

    /// <summary>
    /// Counts per status the runs of one table that <paramref name="query"/> matches, in the buckets
    /// <paramref name="boundaries"/> bound, by execution time.
    /// </summary>
    /// <param name="kind">The table to count.</param>
    /// <param name="query">The runs to count; the kind filter is the caller's to apply.</param>
    /// <param name="boundaries">
    /// The bucket edges (UTC), strictly ascending: bucket <c>i</c> holds execution times from <c>boundaries[i]</c>,
    /// inclusive, to <c>boundaries[i + 1]</c>, exclusive.
    /// </param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>One bucket per pair of adjacent edges, in order.</returns>
    /// <remarks>
    /// A run without an execution time is in no bucket. A bucket the store cannot measure carries
    /// <see langword="null"/> counts, never empty ones. The member <see cref="CountJobRunsByBucketAsync"/> is built
    /// on. The default throws.
    /// </remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<IReadOnlyList<JobRunBucket>> CountJobRunBucketRowsAsync(
        JobRunKind kind,
        JobRunQuery query,
        IReadOnlyList<DateTime> boundaries,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>
    /// Counts per status the runs <paramref name="query"/> matches in the buckets <paramref name="boundaries"/>
    /// bound, by execution time, so a future bucket counts the runs scheduled in it.
    /// </summary>
    /// <param name="query">The runs to count.</param>
    /// <param name="boundaries">The bucket edges (UTC), strictly ascending, at least two.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>One bucket per pair of adjacent edges, in order.</returns>
    /// <remarks>
    /// Default method built on <see cref="CountJobRunBucketRowsAsync"/>, one grouped read per table. A bucket is
    /// <see langword="null"/> when either table could not measure it. The buckets count by execution time, while the
    /// Overview figures count by when a run ended, so the two can place one run on different days.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="boundaries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="boundaries"/> has fewer than two edges or is not strictly ascending.</exception>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    async Task<IReadOnlyList<JobRunBucket>> CountJobRunsByBucketAsync(
        JobRunQuery query,
        IReadOnlyList<DateTime> boundaries,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsNotNull(boundaries);

        if (boundaries.Count < 2)
        {
            throw new ArgumentException("A bucketed count needs at least two edges.", nameof(boundaries));
        }

        for (var index = 1; index < boundaries.Count; index++)
        {
            if (boundaries[index] <= boundaries[index - 1])
            {
                throw new ArgumentException("The bucket edges must be strictly ascending.", nameof(boundaries));
            }
        }

        var merged = new Dictionary<JobStatus, int>?[boundaries.Count - 1];

        for (var index = 0; index < merged.Length; index++)
        {
            merged[index] = [];
        }

        foreach (var kind in _RunKinds(query))
        {
            var buckets = await CountJobRunBucketRowsAsync(kind, query, boundaries, cancellationToken)
                .ConfigureAwait(false);

            for (var index = 0; index < merged.Length; index++)
            {
                // A bucket one table could not measure is unmeasured as a whole: a partial sum would read as a count.
                if (merged[index] is not { } total || buckets[index].Counts is not { } counts)
                {
                    merged[index] = null;

                    continue;
                }

                foreach (var (status, count) in counts)
                {
                    total[status] = total.GetValueOrDefault(status) + count;
                }
            }
        }

        return
        [
            .. merged.Select(
                (counts, index) =>
                    new JobRunBucket
                    {
                        Start = boundaries[index],
                        End = boundaries[index + 1],
                        Counts = counts,
                    }
            ),
        ];
    }

    /// <summary>Counts one table's <see cref="JobStatus.InProgress"/> runs per claim owner.</summary>
    /// <param name="kind">The table to count.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The count per owner tag; an owner with no running run has no entry.</returns>
    /// <remarks>The member <see cref="CountInProgressJobRunsByOwnerAsync"/> is built on. The default throws.</remarks>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task<IReadOnlyDictionary<string, int>> CountInProgressJobRunRowsByOwnerAsync(
        JobRunKind kind,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException($"{GetType().Name} does not support Jobs dashboard reads.");
    }

    /// <summary>
    /// Counts the <see cref="JobStatus.InProgress"/> runs of both kinds per claim owner, each stopped at
    /// <paramref name="cap"/>.
    /// </summary>
    /// <param name="cap">The cap; the dashboard counts to <see cref="JobRunCount.DefaultCap"/>.</param>
    /// <param name="cancellationToken">Token that aborts the query.</param>
    /// <returns>The capped count per owner tag, the tag the store stamps on a claimed row.</returns>
    /// <remarks>Default method built on <see cref="CountInProgressJobRunRowsByOwnerAsync"/>, one grouped read per table.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is not positive.</exception>
    /// <exception cref="NotSupportedException">The provider does not support dashboard reads.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    async Task<IReadOnlyDictionary<string, JobRunCount>> CountInProgressJobRunsByOwnerAsync(
        int cap = JobRunCount.DefaultCap,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(cap);

        var totals = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var kind in (JobRunKind[])[JobRunKind.TimeJob, JobRunKind.CronJobOccurrence])
        {
            foreach (
                var (owner, count) in await CountInProgressJobRunRowsByOwnerAsync(kind, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                totals[owner] = totals.GetValueOrDefault(owner) + count;
            }
        }

        return totals.ToDictionary(
            static pair => pair.Key,
            pair => JobRunCount.Of(pair.Value, cap),
            StringComparer.Ordinal
        );
    }

    #endregion
}
