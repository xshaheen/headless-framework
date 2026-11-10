// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination;
using Headless.Jobs.Internal;
using Headless.Jobs.Temps;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>The dashboard seam over the host's Jobs services, closed over its entity types.</summary>
internal sealed class JobsDashboardBackend<TTimeJob, TCronJob>(IServiceProvider services) : IJobsDashboardBackend
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly IServiceProvider _services = Argument.IsNotNull(services);

    // Resolved on first use: a dashboard module is built when the shell first reads its manifest, and none of these
    // services needs to exist before a request does.
    private IJobPersistenceProvider<TTimeJob, TCronJob> Store =>
        field ??= _services.GetRequiredService<IJobPersistenceProvider<TTimeJob, TCronJob>>();

    private JobFunctionRegistry Registry => field ??= _services.GetRequiredService<JobFunctionRegistry>();

    private SchedulerOptionsBuilder SchedulerOptions =>
        field ??= _services.GetRequiredService<SchedulerOptionsBuilder>();

    private CronScheduleCache Cron => field ??= _services.GetRequiredService<CronScheduleCache>();

    private IJobScheduler Scheduling => field ??= _services.GetRequiredService<IJobScheduler>();

    private IJobsHostScheduler HostScheduler => field ??= _services.GetRequiredService<IJobsHostScheduler>();

    private JobsRequestSerializationOptions Serialization =>
        field ??= _services.GetRequiredService<JobsRequestSerializationOptions>();

    public TimeZoneInfo SchedulerTimeZone => SchedulerOptions.SchedulerTimeZone;

    public TimeSpan LateAfter => SchedulerOptions.FallbackIntervalChecker;

    public IReadOnlyList<JobsDashboardFunction> Functions =>
        field ??= [
            .. Registry
                .Descriptors.Values.OrderBy(static descriptor => descriptor.FunctionName, StringComparer.Ordinal)
                .Select(descriptor => new JobsDashboardFunction(
                    descriptor.FunctionName,
                    descriptor.ContractVersion,
                    descriptor.Priority,
                    descriptor.MaxConcurrency,
                    Registry.RequestTypes.TryGetValue(descriptor.FunctionName, out var request) ? request.Item1 : null,
                    string.IsNullOrWhiteSpace(descriptor.CronExpression) ? null : descriptor.CronExpression
                )),
        ];

    public Guid? RowIdOf(object row)
    {
        return row switch
        {
            BaseJobEntity entity => entity.Id,
            CronJobOccurrenceEntity<TCronJob> occurrence => occurrence.Id,
            _ => null,
        };
    }

    public Task<JobRunPage> ListRunsAsync(
        JobRunQuery query,
        JobRunCursor? after,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        return Store.ListJobRunsAsync(query, after, pageSize, cancellationToken);
    }

    public Task<JobRunCount> CountRunsAsync(JobRunQuery query, CancellationToken cancellationToken)
    {
        return Store.CountJobRunsAsync(query, cancellationToken: cancellationToken);
    }

    public Task<JobRunSummary?> GetRunAsync(Guid id, CancellationToken cancellationToken)
    {
        return Store.GetJobRunAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetChildIdsAsync(Guid timeJobId, CancellationToken cancellationToken)
    {
        var job = await Store.GetTimeJobByIdAsync(timeJobId, cancellationToken).ConfigureAwait(false);

        return job is null ? [] : [.. job.Children.Select(static child => child.Id)];
    }

    public async Task<IReadOnlyList<JobAttemptEntity>> GetAttemptsAsync(
        JobRunKind kind,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        return kind == JobRunKind.TimeJob
            ? await Store.GetTimeJobAttemptsAsync(id, cancellationToken).ConfigureAwait(false)
            : await Store.GetCronJobOccurrenceAttemptsAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobsDashboardRequest?> GetRequestAsync(
        JobRunKind kind,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        byte[]? bytes;
        string function;
        string contractVersion;

        if (kind == JobRunKind.TimeJob)
        {
            var job = await Store.GetTimeJobByIdAsync(id, cancellationToken).ConfigureAwait(false);

            if (job is null)
            {
                return null;
            }

            (bytes, function, contractVersion) = (job.Request, job.Function, job.ContractVersion);
        }
        else
        {
            var occurrence = await Store.GetCronJobOccurrenceByIdAsync(id, cancellationToken).ConfigureAwait(false);

            if (occurrence is null)
            {
                return null;
            }

            (bytes, function, contractVersion) = (occurrence.Request, occurrence.Function, occurrence.ContractVersion);
        }

        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        // A stored version this host no longer registers is shown, not parsed: binding it to today's type could
        // misreport its fields.
        if (
            Registry.Descriptors.TryGetValue(function, out var descriptor)
            && !string.Equals(contractVersion, descriptor.ContractVersion, StringComparison.Ordinal)
        )
        {
            return new JobsDashboardRequest(string.Empty, $"Unsupported contract version {contractVersion}");
        }

        return new JobsDashboardRequest(JobsHelper.ReadJobRequestAsString(bytes, Serialization), Unsupported: null);
    }

    public Task<IReadOnlyList<JobFailureGroup>> GetFailureGroupsAsync(
        DateTimeOffset recentSince,
        CancellationToken cancellationToken
    )
    {
        return Store.GetJobFailureGroupsAsync(recentSince, cancellationToken: cancellationToken);
    }

    public Task<JobOverviewFigures> GetOverviewFiguresAsync(DateTime now, CancellationToken cancellationToken)
    {
        return Store.GetJobOverviewFiguresAsync(now, LateAfter, cancellationToken);
    }

    public Task<IReadOnlyList<JobFunctionStats>> GetFunctionStatsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken
    )
    {
        return Store.GetJobFunctionStatsAsync(since, cancellationToken);
    }

    public async Task<CronJobEntity?> GetCronJobAsync(Guid id, CancellationToken cancellationToken)
    {
        return await Store.GetCronJobByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CronJobEntity>> GetCronJobsAsync(CancellationToken cancellationToken)
    {
        return await Store.GetCronJobsAsync(predicate: null, cancellationToken).ConfigureAwait(false);
    }

    public CronSchedulePreview Preview(string expression, string? timeZoneId, DateTime fromUtc, int count)
    {
        return Cron.Preview(expression, timeZoneId, fromUtc, count);
    }

    public async Task<IReadOnlyList<JobsDashboardNode>> GetNodesAsync(CancellationToken cancellationToken)
    {
        var membership = _services.GetService<INodeMembership>();

        if (membership is null or NullNodeMembership)
        {
            // Without membership this host is the only node it knows of; a host whose job loops are off claims nothing.
            var runsJobs = HostScheduler is not NoOpJobsHostScheduler;

            return
            [
                new JobsDashboardNode(
                    SchedulerOptions.NodeId,
                    nameof(NodeLivenessState.Alive),
                    Role: null,
                    runsJobs ? Registry.RunFilter.ClaimedFunctions(Registry.Functions.Keys) : [],
                    // Without membership a claim stamps the configured node id.
                    Owner: SchedulerOptions.NodeId,
                    IsLocal: true,
                    SchedulerOptions.MaxConcurrency,
                    LastHeartbeatAt: null
                ),
            ];
        }

        var snapshot = await membership.GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var local = membership.Identity;
        var cluster = membership.ClusterName;

        return
        [
            .. snapshot
                .Where(static node => node.State != NodeLivenessState.Dead)
                .Select(node => new JobsDashboardNode(
                    node.Identity.ToString(),
                    node.State.ToString(),
                    node.Role,
                    JobsNodeMetadata.TryReadClaimedFunctions(node.Metadata, out var claimed) ? claimed : null,
                    NodeOwnerTag.Format(cluster, node.Identity),
                    node.Identity == local,
                    // This host knows its own setting even when a peer runs a build that does not publish it.
                    node.Identity == local
                        ? SchedulerOptions.MaxConcurrency
                        : JobsNodeMetadata.ReadMaxConcurrency(node.Metadata),
                    node.LastHeartbeatAt
                )),
        ];
    }

    public Task<DateTime?> GetLatestChangeMarkerAsync(CancellationToken cancellationToken)
    {
        return Store.GetLatestChangeMarkerAsync(cancellationToken);
    }

    public Task<JobChangeCursor> GetChangeStartCursorAsync(CancellationToken cancellationToken)
    {
        return Store.GetChangeStartCursorAsync(cancellationToken);
    }

    public Task<JobChangePage> ReadChangesAsync(
        JobChangeCursor cursor,
        int pageSize,
        TimeSpan overlapWindow,
        CancellationToken cancellationToken
    )
    {
        return Store.ReadChangesAsync(cursor, pageSize, overlapWindow, cancellationToken);
    }

    public Task<JobRunCount> CountOperationScopeAsync(
        JobOperationScope scope,
        int cap,
        CancellationToken cancellationToken
    )
    {
        return Store.CountJobOperationScopeAsync(scope, cap, cancellationToken);
    }

    public Task<JobOperationChunk> ReadOperationScopeAsync(
        JobOperationScope scope,
        JobOperationCursor? after,
        int chunkSize,
        CancellationToken cancellationToken
    )
    {
        return Store.ReadJobOperationScopeAsync(scope, after, chunkSize, cancellationToken);
    }

    public async Task<JobRequeueOutcome> RequeueAsync(
        JobRunKind kind,
        Guid id,
        DateTime? expectedChangedAt,
        CancellationToken cancellationToken
    )
    {
        if (expectedChangedAt is not { } expected)
        {
            return kind == JobRunKind.TimeJob
                ? await Scheduling.RequeueAsync(id, cancellationToken).ConfigureAwait(false)
                : await Scheduling.RequeueOccurrenceAsync(id, cancellationToken).ConfigureAwait(false);
        }

        // The requeue is the rerun's cause, so the rerun links to it, as a single requeue's does.
        using var activity = JobsDiagnostics.StartSchedule(
            kind == JobRunKind.TimeJob ? JobType.TimeJob : JobType.CronJobOccurrence,
            function: null
        );
        activity?.SetTag("headless.job.id", id.ToString());
        var traceParent = JobTraceParent.Capture(activity);

        var outcome =
            kind == JobRunKind.TimeJob
                ? await Store
                    .RequeueTimeJobIfUnchangedAsync(id, expected, traceParent, cancellationToken)
                    .ConfigureAwait(false)
                : await Store
                    .RequeueCronJobOccurrenceIfUnchangedAsync(id, expected, traceParent, cancellationToken)
                    .ConfigureAwait(false);

        if (outcome == JobRequeueOutcome.Requeued)
        {
            HostScheduler.Restart();
        }

        return outcome;
    }

    public Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        return Scheduling.CancelAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        JobRunKind kind,
        Guid id,
        DateTime expectedChangedAt,
        CancellationToken cancellationToken
    )
    {
        var deleted =
            kind == JobRunKind.TimeJob
                ? await Store
                    .RemoveTimeJobIfUnchangedAsync(id, expectedChangedAt, cancellationToken)
                    .ConfigureAwait(false)
                : await Store
                    .RemoveCronJobOccurrenceIfUnchangedAsync(id, expectedChangedAt, cancellationToken)
                    .ConfigureAwait(false);

        // The scheduler may hold the deleted run as its next one to fire; a restart makes it pick the next again.
        if (
            deleted
            && _services.GetService<JobsExecutionContext>() is { } execution
            && execution.Functions.Any(function => function.JobId == id)
        )
        {
            HostScheduler.Restart();
        }

        return deleted;
    }

    public Task<bool> PauseCronJobAsync(Guid id, CancellationToken cancellationToken)
    {
        return Scheduling.PauseCronAsync(id, cancellationToken);
    }

    public Task<bool> ResumeCronJobAsync(Guid id, CancellationToken cancellationToken)
    {
        return Scheduling.ResumeCronAsync(id, cancellationToken);
    }

    public async Task<Guid> RunCronJobNowAsync(Guid id, CancellationToken cancellationToken)
    {
        // Unlike an occurrence the scheduler generates, a run-now has a cause: the dashboard request. Its run links to it.
        using var activity = JobsDiagnostics.StartSchedule(JobType.CronJobOccurrence, function: null);
        var now = _services.GetRequiredService<TimeProvider>().GetUtcNow();
        var occurrence = new CronJobOccurrenceEntity<TCronJob>
        {
            Id = _services.GetRequiredService<IGuidGenerator>().Create(),
            Status = JobStatus.Idle,
            LockedUntil = null,
            CronJobId = id,
            TraceParent = JobTraceParent.Capture(activity),
            CreatedAt = now,
            UpdatedAt = now,
        };
        activity?.SetTag("headless.job.id", occurrence.Id.ToString());

        // The store stamps the due time on its own clock: if the immediate acquire below loses (a saturated cluster
        // limit, say), the fallback sweep must find the occurrence due on the database clock, not a skew later.
        await Store.InsertRunNowCronJobOccurrenceAsync(occurrence, cancellationToken).ConfigureAwait(false);

        var acquired = await Store
            .AcquireImmediateCronOccurrencesAsync([occurrence.Id], cancellationToken)
            .ConfigureAwait(false);

        if (acquired.Length > 0)
        {
            var claimed = acquired[0];
            var context = new JobExecutionState
            {
                ParentId = claimed.CronJobId,
                FunctionName = claimed.Function,
                ContractVersion = claimed.ContractVersion,
                CorrelationId = claimed.CorrelationId,
                CausationId = claimed.CausationId,
                TraceParent = claimed.TraceParent,
                TenantId = claimed.TenantId,
                JobId = claimed.Id,
                Type = JobType.CronJobOccurrence,
                Retries = claimed.CronJob.Retries,
                RetryIntervals = claimed.CronJob.RetryIntervals,
                ExecutionTime = claimed.ExecutionTime,
                ClaimOwner = claimed.OwnerId,
            };

            // Canonical hydration: also stamps CachedMaxConcurrency, so an on-demand run acquires the same
            // per-function concurrency gate as scheduler pickups instead of silently bypassing the limit.
            JobsExecutionContext.CacheFunctionReferences(context, Registry);

            await _services
                .GetRequiredService<IJobsDispatcher>()
                .DispatchAsync([context], cancellationToken)
                .ConfigureAwait(false);
        }

        return occurrence.Id;
    }

    public async Task<string?> UpdateCronScheduleAsync(
        Guid id,
        string expression,
        string? timeZoneId,
        CancellationToken cancellationToken
    )
    {
        var cronJob = await Store.GetCronJobByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (cronJob is null)
        {
            return "This cron job no longer exists";
        }

        cronJob.Expression = expression;
        cronJob.TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId;

        var result = await _services
            .GetRequiredService<ICronJobManager<TCronJob>>()
            .UpdateAsync(cronJob, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSucceeded ? null : result.Exception.Message;
    }

    public async Task<Guid> AddTimeJobAsync(JobsDashboardTimeJobDraft draft, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(draft);

        var root = _BuildTimeJob(draft, isRoot: true);
        var created = await _services
            .GetRequiredService<ITimeJobManager<TTimeJob>>()
            .AddAsync(root, cancellationToken)
            .ConfigureAwait(false);

        return created.Id;
    }

    public async Task<Guid> AddCronJobAsync(JobsDashboardCronJobDraft draft, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(draft);

        var cronJob = new TCronJob
        {
            Function = _RegisteredFunction(draft.Function),
            Expression = draft.Expression,
            TimeZoneId = string.IsNullOrWhiteSpace(draft.TimeZoneId) ? null : draft.TimeZoneId,
            Request = _Request(draft.Function, draft.RequestJson),
            Description = draft.Description,
        };

        var created = await _services
            .GetRequiredService<ICronJobManager<TCronJob>>()
            .AddAsync(cronJob, cancellationToken)
            .ConfigureAwait(false);

        return created.Id;
    }

    public Task<IReadOnlyList<JobRunBucket>> CountRunsByBucketAsync(
        JobRunQuery query,
        IReadOnlyList<DateTime> boundaries,
        CancellationToken cancellationToken
    )
    {
        return Store.CountJobRunsByBucketAsync(query, boundaries, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, JobRunCount>> CountRunningByOwnerAsync(CancellationToken cancellationToken)
    {
        return Store.CountInProgressJobRunsByOwnerAsync(cancellationToken: cancellationToken);
    }

    private TTimeJob _BuildTimeJob(JobsDashboardTimeJobDraft draft, bool isRoot)
    {
        var job = new TTimeJob
        {
            Function = _RegisteredFunction(draft.Function),
            ExecutionTime = draft.ExecutionTime is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
            Request = _Request(draft.Function, draft.RequestJson),
            Description = draft.Description,
            Retries = draft.Retries,
            RetryIntervals = draft.RetryIntervals,
            RunCondition = isRoot ? null : draft.RunCondition,
        };

        foreach (var child in draft.Children)
        {
            job.Children.Add(_BuildTimeJob(child, isRoot: false));
        }

        return job;
    }

    private string _RegisteredFunction(string function)
    {
        return Registry.Descriptors.ContainsKey(function)
            ? function
            : throw new ArgumentException($"No function named '{function}' is registered.", nameof(function));
    }

    // The request is bound to the function's request type before it is stored, so a payload the handler could not
    // read is refused here rather than failing every run.
    private byte[]? _Request(string function, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        if (!Registry.RequestTypes.TryGetValue(function, out var requestType))
        {
            throw new ArgumentException($"Function '{function}' takes no request.", nameof(json));
        }

        object? value;

        try
        {
            value = JsonSerializer.Deserialize(json, Serialization.SerializerOptions.GetTypeInfo(requestType.Item2));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                $"The request is not a valid {requestType.Item1}: {exception.Message}",
                nameof(json),
                exception
            );
        }

        return value is null ? null : JobsHelper.CreateJobRequest(value, requestType.Item2, Serialization);
    }
}
