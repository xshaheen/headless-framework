// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Infrastructure.Dashboard;

internal sealed class JobsDashboardRepository<TTimeJob, TCronJob>(
    JobsExecutionContext executionContext,
    IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
    IJobsHostScheduler jobsHostScheduler,
    IJobsNotificationHubSender notificationHubSender,
    DashboardOptionsBuilder dashboardOptions,
    IJobsDispatcher dispatcher,
    JobFunctionRegistry functionRegistry,
    TimeProvider timeProvider,
    IGuidGenerator guidGenerator,
    IServiceProvider serviceProvider,
    JobsRequestSerializationOptions serializationOptions
) : IJobsDashboardRepository<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly IServiceProvider _serviceProvider = Argument.IsNotNull(serviceProvider);
    private readonly JobsRequestSerializationOptions _serializationOptions = Argument.IsNotNull(serializationOptions);

    private readonly IJobPersistenceProvider<TTimeJob, TCronJob> _persistenceProvider = Argument.IsNotNull(
        persistenceProvider
    );

    private readonly IJobsHostScheduler _jobsHostScheduler = Argument.IsNotNull(jobsHostScheduler);
    private readonly IJobsDispatcher _dispatcher = Argument.IsNotNull(dispatcher);
    private readonly JobFunctionRegistry _functionRegistry = Argument.IsNotNull(functionRegistry);
    private readonly IJobsNotificationHubSender _notificationHubSender = Argument.IsNotNull(notificationHubSender);
    private readonly JobsExecutionContext _executionContext = Argument.IsNotNull(executionContext);
    private readonly DashboardOptionsBuilder _dashboardOptions = Argument.IsNotNull(dashboardOptions);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private readonly IGuidGenerator _guidGenerator = Argument.IsNotNull(guidGenerator);

    // Graph endpoints materialize one entry per day across [pastDays, futureDays], so an unclamped span
    // could drive multi-million-object allocation independent of stored row count. Clamp the request-supplied
    // offsets to a bounded window (±1 year) before computing the range.
    private const int _MaxGraphRangeDays = 366;

    private static int _ClampGraphDays(int days)
    {
        return Math.Clamp(days, -_MaxGraphRangeDays, _MaxGraphRangeDays);
    }

    // Inverted ranges (pastDays > futureDays) would otherwise pass a negative count to Enumerable.Range and
    // throw; clamp the count to 0 so a nonsensical range yields an empty series instead of a 500.
    private static int _GraphDayCount(DateTime startDate, DateTime endDate)
    {
        return Math.Max(0, (endDate - startDate).Days + 1);
    }

    public async Task<TTimeJob[]> GetTimeJobsAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceProvider.GetTimeJobsAsync(predicate: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PaginationResult<TTimeJob>> GetTimeJobsPaginatedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        return await _persistenceProvider
            .GetTimeJobsPaginatedAsync(predicate: null, pageNumber, pageSize, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IList<(JobStatus Status, int Count)>> GetTimeJobFullDataAsync(CancellationToken cancellationToken)
    {
        var statusCounts = await _persistenceProvider
            .GetTimeJobStatusCountsAsync(cancellationToken)
            .ConfigureAwait(false);

        // Create a dictionary for quick lookup
        var statusCountLookup = statusCounts.ToDictionary(x => x.Status, x => x.Count);

        // Ensure all statuses are included, even those with 0 count
        var result = Enum.GetValues<JobStatus>()
            .Select(status => (Status: status, Count: statusCountLookup.GetValueOrDefault(status, 0)))
            .ToList();

        return result;
    }

    public async Task<IList<JobGraphData>> GetTimeJobsGraphSpecificDataAsync(
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    )
    {
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var startDate = today.AddDays(_ClampGraphDays(pastDays));
        var endDate = today.AddDays(_ClampGraphDays(futureDays));

        // Storage-side grouped counts: only the observed (date, status) pairs cross the boundary instead of
        // every job row in the window.
        var dailyCounts = await _persistenceProvider
            .GetTimeJobDailyStatusCountsAsync(startDate, endDate, cancellationToken)
            .ConfigureAwait(false);

        // Get all possible statuses once
        var allStatuses = Enum.GetValues<JobStatus>();

        // Build the final result: one entry per date, with all statuses filled
        var allDates = Enumerable
            .Range(0, _GraphDayCount(startDate, endDate))
            .Select(offset => startDate.AddDays(offset))
            .ToList();

        var groupedData = dailyCounts
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Status, s => s.Count));

        var finalData = allDates.ConvertAll(date =>
        {
            var statusCounts = groupedData.TryGetValue(date, out var statusData) ? statusData : [];

            var results = allStatuses
                .Select(status => new JobStatusCount(status, statusCounts.GetValueOrDefault(status, 0)))
                .ToArray();

            return new JobGraphData { Date = date, Results = results };
        });

        return finalData;
    }

    public async Task<IList<JobGraphData>> GetCronJobsGraphSpecificDataByIdAsync(
        Guid id,
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    )
    {
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var startDate = today.AddDays(_ClampGraphDays(pastDays));
        var endDate = today.AddDays(_ClampGraphDays(futureDays));

        var cronJobOccurrences = await _persistenceProvider
            .GetAllCronJobOccurrencesAsync(
                x => x.CronJobId == id && x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate,
                cancellationToken
            )
            .ConfigureAwait(false);

        var allStatuses = Enum.GetValues<JobStatus>();

        var rawData = cronJobOccurrences
            .GroupBy(x => new { x.ExecutionTime.Date, x.Status })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.Status,
                Count = g.Count(),
            })
            .ToList();

        var allDates = Enumerable
            .Range(0, _GraphDayCount(startDate, endDate))
            .Select(offset => startDate.AddDays(offset))
            .ToList();

        var groupedData = rawData
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Status, s => s.Count));

        var finalData = allDates.ConvertAll(date =>
        {
            var statusCounts = groupedData.TryGetValue(date, out var statusData) ? statusData : [];

            var results = allStatuses
                .Select(status => new JobStatusCount(status, statusCounts.GetValueOrDefault(status, 0)))
                .ToArray();

            return new JobGraphData { Date = date, Results = results };
        });

        return finalData;
    }

    public async Task<IList<(JobStatus Status, int Count)>> GetCronJobFullDataAsync(CancellationToken cancellationToken)
    {
        var statusCounts = await _persistenceProvider
            .GetCronOccurrenceStatusCountsAsync(cancellationToken)
            .ConfigureAwait(false);

        var statusCountLookup = statusCounts.ToDictionary(x => x.Status, x => x.Count);

        var result = Enum.GetValues<JobStatus>()
            .Select(status => (Status: status, Count: statusCountLookup.GetValueOrDefault(status, 0)))
            .ToList();

        return result;
    }

    public async Task<IList<JobGraphData>> GetCronJobsGraphSpecificDataAsync(
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    )
    {
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var startDate = today.AddDays(_ClampGraphDays(pastDays));
        var endDate = today.AddDays(_ClampGraphDays(futureDays));

        // Storage-side grouped counts: only the observed (date, status) pairs cross the boundary instead of
        // every occurrence row in the window.
        var dailyCounts = await _persistenceProvider
            .GetCronOccurrenceDailyStatusCountsAsync(startDate, endDate, cancellationToken)
            .ConfigureAwait(false);

        var allStatuses = Enum.GetValues<JobStatus>();

        var allDates = Enumerable
            .Range(0, _GraphDayCount(startDate, endDate))
            .Select(offset => startDate.AddDays(offset))
            .ToList();

        var groupedData = dailyCounts
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Status, s => s.Count));

        var finalData = allDates.ConvertAll(date =>
        {
            var statusCounts = groupedData.TryGetValue(date, out var statusData) ? statusData : [];

            var results = allStatuses
                .Select(status => new JobStatusCount(status, statusCounts.GetValueOrDefault(status, 0)))
                .ToArray();

            return new JobGraphData { Date = date, Results = results };
        });

        return finalData;
    }

    public async Task<IList<(int, int)>> GetLastWeekJobStatusesAsync(CancellationToken cancellationToken = default)
    {
        var endDate = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var startDate = endDate.AddDays(-7);

        var timeJobCounts = await _persistenceProvider
            .GetTimeJobDailyStatusCountsAsync(startDate, endDate, cancellationToken)
            .ConfigureAwait(false);

        var cronOccurrenceCounts = await _persistenceProvider
            .GetCronOccurrenceDailyStatusCountsAsync(startDate, endDate, cancellationToken)
            .ConfigureAwait(false);

        // Merge both sources' grouped counts: (done-or-due-done, failed, total) across time jobs and occurrences.
        var allCounts = timeJobCounts.Concat(cronOccurrenceCounts).ToList();

        var doneOrDueDoneCount = allCounts
            .Where(x => x.Status is JobStatus.Succeeded or JobStatus.DueDone)
            .Sum(x => x.Count);
        var failedCount = allCounts.Where(x => x.Status == JobStatus.Failed).Sum(x => x.Count);
        var totalCount = allCounts.Sum(x => x.Count);

        return [(0, doneOrDueDoneCount), (1, failedCount), (2, totalCount)];
    }

    public async Task<IList<(JobStatus, int)>> GetOverallJobStatusesAsync(CancellationToken cancellationToken = default)
    {
        var timeJobCounts = await _persistenceProvider
            .GetTimeJobStatusCountsAsync(cancellationToken)
            .ConfigureAwait(false);

        var cronOccurrenceCounts = await _persistenceProvider
            .GetCronOccurrenceStatusCountsAsync(cancellationToken)
            .ConfigureAwait(false);

        // Both sources are ordered by recency (most recent execution time per status, first appearance wins), so
        // a status keeps the position it earned in the source that most recently executed it — the same order the
        // materialized rows produced.
        var combined = timeJobCounts
            .Concat(cronOccurrenceCounts)
            .GroupBy(x => x.Status)
            .Select(g => (g.Key, g.Sum(x => x.Count)))
            .ToList();

        return combined;
    }

    public async Task<IList<(string, int)>> GetMachineJobsAsync(CancellationToken cancellationToken = default)
    {
        var timeJobOwners = await _persistenceProvider
            .GetTimeJobLockedOwnerCountsAsync(cancellationToken)
            .ConfigureAwait(false);
        var cronOccurrenceOwners = await _persistenceProvider
            .GetCronOccurrenceLockedOwnerCountsAsync(cancellationToken)
            .ConfigureAwait(false);

        // Owner identity is case-insensitive (node@incarnation is compared OrdinalIgnoreCase by coordination),
        // so fold the two sources case-insensitively; first source's casing wins, matching the previous merge.
        return timeJobOwners
            .Concat(cronOccurrenceOwners)
            .GroupBy(x => x.OwnerId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Sum(x => x.Count)))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    public async Task<IReadOnlyList<LiveNodeView>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
    {
        // The coordination provider is optional: the in-memory / single-process path registers no INodeMembership,
        // and the NullNodeMembership default never reports live nodes. Either way the panel renders empty.
        var membership = _serviceProvider.GetService<INodeMembership>();

        if (membership is null or NullNodeMembership)
        {
            return [];
        }

        var snapshot = await membership.GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return ProjectLiveNodes(snapshot);
    }

    /// <summary>Projects a coordination liveness snapshot into the dashboard node view. Pure — testable in isolation.</summary>
    internal static IReadOnlyList<LiveNodeView> ProjectLiveNodes(IReadOnlyList<NodeLivenessSnapshot> snapshot)
    {
        var views = new List<LiveNodeView>(snapshot.Count);

        foreach (var node in snapshot)
        {
            views.Add(
                new LiveNodeView
                {
                    Identity = node.Identity.ToString(),
                    State = node.State.ToString(),
                    Role = node.Role,
                    LastBeat = _ExtractLastBeat(node.Metadata),
                    Metadata = node.Metadata,
                }
            );
        }

        return views;
    }

    private static string? _ExtractLastBeat(IReadOnlyDictionary<string, string> metadata)
    {
        // Last-beat is provider-supplied and best-effort; probe the common metadata keys without assuming any one.
        foreach (var key in _LastBeatMetadataKeys)
        {
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static readonly string[] _LastBeatMetadataKeys =
    [
        "last_beat",
        "lastBeat",
        "last_heartbeat",
        "lastHeartbeat",
    ];

    public async Task<CronJobEntity[]> GetCronJobsAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceProvider.GetCronJobsAsync(predicate: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PaginationResult<CronJobEntity>> GetCronJobsPaginatedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        // We need to cast TCronJob[] to CronJobEntity[] for the pagination result
        var result = await _persistenceProvider
            .GetCronJobsPaginatedAsync(predicate: null, pageNumber, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return new PaginationResult<CronJobEntity>
        {
            Items = result.Items,
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
        };
    }

    public async Task AddOnDemandCronJobOccurrenceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var onDemandOccurrence = new CronJobOccurrenceEntity<TCronJob>
        {
            Id = _guidGenerator.Create(),
            Status = JobStatus.Idle,
            ExecutionTime = now.UtcDateTime,
            LockedUntil = null,
            CronJobId = id,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _persistenceProvider
            .InsertCronJobOccurrencesAsync([onDemandOccurrence], cancellationToken)
            .ConfigureAwait(false);

        // Acquire and run immediately
        var acquired = await _persistenceProvider
            .AcquireImmediateCronOccurrencesAsync([onDemandOccurrence.Id], cancellationToken)
            .ConfigureAwait(false);

        CronJobOccurrenceEntity<TCronJob>? acquiredOccurrence = null;

        if (acquired.Length > 0)
        {
            var occurrence = acquired[0];
            acquiredOccurrence = occurrence;
            var context = new JobExecutionState
            {
                ParentId = occurrence.CronJobId,
                FunctionName = occurrence.Function,
                ContractVersion = occurrence.ContractVersion,
                CorrelationId = occurrence.CorrelationId,
                CausationId = occurrence.CausationId,
                TenantId = occurrence.TenantId,
                JobId = occurrence.Id,
                Type = JobType.CronJobOccurrence,
                Retries = occurrence.CronJob.Retries,
                RetryIntervals = occurrence.CronJob.RetryIntervals,
                ExecutionTime = occurrence.ExecutionTime,
            };

            // Canonical hydration: also stamps CachedMaxConcurrency, so an on-demand run acquires the same
            // per-function concurrency gate as scheduler pickups instead of silently bypassing the limit.
            JobsExecutionContext.CacheFunctionReferences(context, _functionRegistry);

            await _dispatcher.DispatchAsync([context], cancellationToken).ConfigureAwait(false);
        }

        // Notify dashboard about the new occurrence (prefer the acquired version if available)
        if (_notificationHubSender != null)
        {
            await _notificationHubSender
                .AddCronOccurrenceAsync(id, acquiredOccurrence ?? onDemandOccurrence)
                .ConfigureAwait(false);
        }
    }

    public async Task<CronJobOccurrenceEntity<TCronJob>[]> GetCronJobsOccurrencesAsync(
        Guid cronJobId,
        CancellationToken cancellationToken = default
    )
    {
        return await _persistenceProvider
            .GetAllCronJobOccurrencesAsync(x => x.CronJobId == cronJobId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PaginationResult<CronJobOccurrenceEntity<TCronJob>>> GetCronJobsOccurrencesPaginatedAsync(
        Guid cronJobId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        return await _persistenceProvider
            .GetAllCronJobOccurrencesPaginatedAsync(
                x => x.CronJobId == cronJobId,
                pageNumber,
                pageSize,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<IList<CronOccurrenceJobGraphData>> GetCronJobsOccurrencesGraphDataAsync(
        Guid guid,
        CancellationToken cancellationToken = default
    )
    {
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;

        var statusCounts = await _persistenceProvider
            .GetCronOccurrenceGraphStatusCountsAsync(guid, today, cancellationToken)
            .ConfigureAwait(false);
        var boundaries = statusCounts.Where(x => x.IsRangeBoundary).Select(x => x.Date).ToArray();
        var startDate = boundaries.Min();
        var endDate = boundaries.Max();
        var groupedData = statusCounts
            .Where(x => !x.IsRangeBoundary)
            .GroupBy(x => x.Date)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => new JobStatusCount(x.Status, x.Count)).ToArray()
            );
        var allDates = Enumerable
            .Range(0, _GraphDayCount(startDate, endDate))
            .Select(offset => startDate.AddDays(offset))
            .ToList();

        var finalData = allDates.ConvertAll(date => new CronOccurrenceJobGraphData
        {
            Date = date,
            Results = groupedData.GetValueOrDefault(date, []),
        });

        return finalData;
    }

    public async Task DeleteCronJobOccurrenceByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _persistenceProvider.RemoveCronJobOccurrencesAsync([id], cancellationToken).ConfigureAwait(false);

        if (_executionContext.Functions.Any(x => x.JobId == id))
        {
            _jobsHostScheduler.Restart();
        }
    }

    public async Task<(string, int)> GetJobRequestByIdAsync(
        Guid jobId,
        JobType jobType,
        CancellationToken cancellationToken = default
    )
    {
        byte[]? jsonRequestBytes;
        string functionName;
        string contractVersion;

        if (jobType == JobType.TimeJob)
        {
            var timeJob = await _persistenceProvider
                .GetTimeJobByIdAsync(jobId, cancellationToken)
                .ConfigureAwait(false);

            if (timeJob == null)
            {
                return (string.Empty, 0);
            }

            jsonRequestBytes = timeJob.Request;
            functionName = timeJob.Function;
            contractVersion = timeJob.ContractVersion;
        }
        else
        {
            var cronJob = await _persistenceProvider
                .GetCronJobByIdAsync(jobId, cancellationToken)
                .ConfigureAwait(false);

            if (cronJob == null)
            {
                return (string.Empty, 0);
            }

            jsonRequestBytes = cronJob.Request;
            functionName = cronJob.Function;
            contractVersion = cronJob.ContractVersion;
        }

        if (jsonRequestBytes == null)
        {
            return (string.Empty, 0);
        }

        if (
            _functionRegistry.Descriptors.TryGetValue(functionName, out var descriptor)
            && !string.Equals(contractVersion, descriptor.ContractVersion, StringComparison.Ordinal)
        )
        {
            return (
                $"Unsupported stored Jobs contract '{functionName}' version '{contractVersion}'; this node registers '{descriptor.ContractVersion}'. Request was not deserialized.",
                2
            );
        }

        var jsonRequest = JobsHelper.ReadJobRequestAsString(jsonRequestBytes, _serializationOptions);

        if (!_functionRegistry.RequestTypes.TryGetValue(functionName, out var functionTypeContext))
        {
            return (jsonRequest, 2);
        }

        try
        {
            JsonSerializer.Deserialize(jsonRequest, functionTypeContext.Item2, _dashboardOptions.DashboardJsonOptions);
            return (jsonRequest, 1);
        }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
        catch
        {
            return (jsonRequest, 2);
        }
#pragma warning restore ERP022
    }

    public IEnumerable<(string, (string, string, JobPriority))> GetJobFunctions()
    {
        foreach (var jobFunction in _functionRegistry.Functions.Select(x => new { x.Key, x.Value.Priority }))
        {
            if (_functionRegistry.RequestTypes.TryGetValue(jobFunction.Key, out var functionTypeContext))
            {
                // Example JSON is cached per request Type inside the generator (a Type's shape never changes at
                // runtime), so repeated dashboard requests stop re-running reflection per function.
                JsonExampleGenerator.TryGenerateExampleJson(functionTypeContext.Item2, out var exampleJson);
                yield return (jobFunction.Key, (functionTypeContext.Item1, exampleJson, jobFunction.Priority));
            }
            else
            {
                yield return (jobFunction.Key, (string.Empty, string.Empty, jobFunction.Priority));
            }
        }
    }
}
