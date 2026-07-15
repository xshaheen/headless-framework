// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination;
using Headless.Jobs.DashboardDtos;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
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
    IServiceProvider serviceProvider
) : IJobsDashboardRepository<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly IServiceProvider _serviceProvider = Argument.IsNotNull(serviceProvider);
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

    // Graph endpoints materialize one entry per day across [pastDays, futureDays], so an unclamped span
    // could drive multi-million-object allocation independent of stored row count. Clamp the request-supplied
    // offsets to a bounded window (±1 year) before computing the range.
    private const int _MaxGraphRangeDays = 366;

    private static int _ClampGraphDays(int days) => Math.Clamp(days, -_MaxGraphRangeDays, _MaxGraphRangeDays);

    // Inverted ranges (pastDays > futureDays) would otherwise pass a negative count to Enumerable.Range and
    // throw; clamp the count to 0 so a nonsensical range yields an empty series instead of a 500.
    private static int _GraphDayCount(DateTime startDate, DateTime endDate) =>
        Math.Max(0, (endDate - startDate).Days + 1);

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
        var timeJobs = await _persistenceProvider
            .GetTimeJobsAsync(predicate: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var allStatuses = Enum.GetValues<JobStatus>();

        // Group by status and get counts
        var rawData = timeJobs.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToList();

        // Create a dictionary for quick lookup
        var statusCounts = rawData.ToDictionary(x => x.Status, x => x.Count);

        // Ensure all statuses are included, even those with 0 count
        var result = allStatuses
            .Select(status => (Status: status, Count: statusCounts.GetValueOrDefault(status, 0)))
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

        var timeJobs = await _persistenceProvider
            .GetTimeJobsAsync(
                x =>
                    (x.ExecutionTime != null)
                    && x.ExecutionTime.Value.Date >= startDate
                    && x.ExecutionTime.Value.Date <= endDate,
                cancellationToken
            )
            .ConfigureAwait(false);

        // Get all possible statuses once
        var allStatuses = Enum.GetValues<JobStatus>();

        var rawData = timeJobs
            .GroupBy(x => new { x.ExecutionTime!.Value.Date, x.Status })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.Status,
                Count = g.Count(),
            })
            .ToList();

        // Build the final result: one entry per date, with all statuses filled
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
                .Select(status => Tuple.Create((int)status, statusCounts.GetValueOrDefault(status, 0)))
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
                .Select(status => Tuple.Create((int)status, statusCounts.GetValueOrDefault(status, 0)))
                .ToArray();

            return new JobGraphData { Date = date, Results = results };
        });

        return finalData;
    }

    public async Task<IList<(JobStatus Status, int Count)>> GetCronJobFullDataAsync(CancellationToken cancellationToken)
    {
        var cronJobOccurrences = await _persistenceProvider
            .GetAllCronJobOccurrencesAsync(predicate: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var allStatuses = Enum.GetValues<JobStatus>();

        var rawData = cronJobOccurrences
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToList();

        var statusCounts = rawData.ToDictionary(x => x.Status, x => x.Count);

        var result = allStatuses
            .Select(status => (Status: status, Count: statusCounts.GetValueOrDefault(status, 0)))
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

        var cronJobOccurrences = await _persistenceProvider
            .GetAllCronJobOccurrencesAsync(
                x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate,
                cancellationToken: cancellationToken
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
                .Select(status => Tuple.Create((int)status, statusCounts.GetValueOrDefault(status, 0)))
                .ToArray();

            return new JobGraphData { Date = date, Results = results };
        });

        return finalData;
    }

    public async Task<IList<(int, int)>> GetLastWeekJobStatusesAsync(CancellationToken cancellationToken = default)
    {
        var endDate = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var startDate = endDate.AddDays(-7);

        var timeJobs = await _persistenceProvider
            .GetTimeJobsAsync(
                x =>
                    (x.ExecutionTime != null)
                    && x.ExecutionTime.Value.Date >= startDate
                    && x.ExecutionTime.Value.Date <= endDate,
                cancellationToken
            )
            .ConfigureAwait(false);

        var timeJobStatuses = timeJobs.Select(x => x.Status).ToList();

        var cronJobOccurrences = await _persistenceProvider
            .GetAllCronJobOccurrencesAsync(
                x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate,
                cancellationToken
            )
            .ConfigureAwait(false);

        var cronJobStatuses = cronJobOccurrences.Select(x => x.Status).ToList();

        // Merge all statuses into one list
        var allStatuses = timeJobStatuses.Concat(cronJobStatuses).ToList();

        // Count per type
        var doneOrDueDoneCount = allStatuses.Count(x => x is JobStatus.Succeeded or JobStatus.DueDone);
        var failedCount = allStatuses.Count(x => x == JobStatus.Failed);
        var totalCount = allStatuses.Count;

        return [(0, doneOrDueDoneCount), (1, failedCount), (2, totalCount)];
    }

    public async Task<IList<(JobStatus, int)>> GetOverallJobStatusesAsync(CancellationToken cancellationToken = default)
    {
        var timeJobs = await _persistenceProvider
            .GetTimeJobsAsync(predicate: null, cancellationToken)
            .ConfigureAwait(false);

        var cronJobOccurrences = await _persistenceProvider
            .GetAllCronJobOccurrencesAsync(predicate: null, cancellationToken)
            .ConfigureAwait(false);

        // Combine counts using LINQ GroupBy across both sources
        var combined = timeJobs
            .Select(x => x.Status)
            .Concat(cronJobOccurrences.Select(x => x.Status))
            .GroupBy(status => status)
            .Select(g => (g.Key, g.Count()))
            .ToList();

        return combined;
    }

    public async Task<IList<(string, int)>> GetMachineJobsAsync(CancellationToken cancellationToken = default)
    {
        var timeJobs = await _persistenceProvider
            .GetTimeJobsAsync(x => x.LockedUntil != null, cancellationToken)
            .ConfigureAwait(false);
        var cronJobOccurrences = await _persistenceProvider
            .GetAllCronJobOccurrencesAsync(x => x.LockedUntil != null, cancellationToken)
            .ConfigureAwait(false);

        // Combine counts using LINQ GroupBy across both sources, filtering out null lock holders
        return timeJobs
            .Select(x => x.OwnerId)
            .Concat(cronJobOccurrences.Select(x => x.OwnerId))
            .Where(holder => holder is not null)
            .GroupBy(holder => holder!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Count()))
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
#pragma warning disable RCS1158 // Static member in generic type should use a type parameter
    internal static IReadOnlyList<LiveNodeView> ProjectLiveNodes(IReadOnlyList<NodeLivenessSnapshot> snapshot)
#pragma warning restore RCS1158
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

        return new PaginationResult<CronJobEntity>(result.Items, result.TotalCount, result.PageNumber, result.PageSize);
    }

    public async Task AddOnDemandCronJobOccurrenceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var onDemandOccurrence = new CronJobOccurrenceEntity<TCronJob>
        {
            Id = Guid.NewGuid(),
            Status = JobStatus.Idle,
            ExecutionTime = now,
            LockedUntil = null,
            CronJobId = id,
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
                FunctionName = occurrence.CronJob.Function,
                JobId = occurrence.Id,
                Type = JobType.CronJobOccurrence,
                Retries = occurrence.CronJob.Retries,
                RetryIntervals = occurrence.CronJob.RetryIntervals,
                ExecutionTime = occurrence.ExecutionTime,
            };

            // Populate cached delegate and priority so the dispatcher can execute the job
            if (_functionRegistry.Functions.TryGetValue(context.FunctionName, out var tickerItem))
            {
                context.CachedDelegate = tickerItem.Delegate;
                context.CachedPriority = tickerItem.Priority;
            }

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
        const int maxTotalDays = 14;
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;

        // Single DB query — split into past/today/future in memory
        var allOccurrences = await _persistenceProvider
            .GetAllCronJobOccurrencesAsync(x => x.CronJobId == guid, cancellationToken)
            .ConfigureAwait(false);

        var grouped = allOccurrences
            .GroupBy(x => x.ExecutionTime.Date)
            .Select(group => new CronOccurrenceJobGraphData
            {
                Date = group.Key,
                Results =
                [
                    .. group
                        .GroupBy(x => x.Status)
                        .Select(statusGroup => Tuple.Create((int)statusGroup.Key, statusGroup.Count())),
                ],
            })
            .ToList();

        var pastData = grouped.Where(d => d.Date < today).OrderBy(d => d.Date).ToList();
        var todayData =
            grouped.FirstOrDefault(d => d.Date == today)
            ?? new CronOccurrenceJobGraphData { Date = today, Results = [] };
        var futureData = grouped.Where(d => d.Date > today).OrderBy(d => d.Date).ToList();

        var pastDaysWithData = pastData.Count;
        var futureDaysWithData = futureData.Count;

        const int remainingSlots = maxTotalDays - 1; // Exclude today
        var emptyPastSlots = Math.Max(0, (remainingSlots - futureDaysWithData) / 2);
        var emptyFutureSlots = Math.Max(0, remainingSlots - pastDaysWithData - emptyPastSlots);

        List<CronOccurrenceJobGraphData> emptyPastDays = [];
        if (emptyPastSlots > 0)
        {
            var firstPastDate = pastData.FirstOrDefault()?.Date ?? today.AddDays(-1);
            for (var i = 1; i <= emptyPastSlots; i++)
            {
                emptyPastDays.Add(new CronOccurrenceJobGraphData { Date = firstPastDate.AddDays(-i), Results = [] });
            }
        }

        List<CronOccurrenceJobGraphData> emptyFutureDays = [];
        if (emptyFutureSlots > 0)
        {
            var lastFutureDate = futureData.LastOrDefault()?.Date ?? today.AddDays(1);
            for (var i = 1; i <= emptyFutureSlots; i++)
            {
                emptyFutureDays.Add(new CronOccurrenceJobGraphData { Date = lastFutureDate.AddDays(i), Results = [] });
            }
        }

        var completeData = emptyPastDays
            .Concat(pastData)
            .Append(todayData)
            .Concat(futureData)
            .Concat(emptyFutureDays)
            .OrderBy(d => d.Date)
            .Take(maxTotalDays)
            .ToList();

        if (completeData.Count == 0)
        {
            return completeData;
        }

        var startDate = completeData[0].Date;
        var endDate = completeData[^1].Date;
        var allDates = Enumerable
            .Range(0, _GraphDayCount(startDate, endDate))
            .Select(offset => startDate.AddDays(offset))
            .ToList();

        var finalData = allDates.ConvertAll(date =>
            completeData.FirstOrDefault(d => d.Date == date)
            ?? new CronOccurrenceJobGraphData { Date = date, Results = [] }
        );

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
        }

        if (jsonRequestBytes == null)
        {
            return (string.Empty, 0);
        }

        var jsonRequest = JobsHelper.ReadJobRequestAsString(jsonRequestBytes);

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
