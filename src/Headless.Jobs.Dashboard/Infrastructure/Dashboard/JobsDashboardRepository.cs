using Headless.Checks;
using Headless.Jobs.DashboardDtos;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

namespace Headless.Jobs.Infrastructure.Dashboard;

internal sealed class JobsDashboardRepository<TTimeJob, TCronJob>(
    JobsExecutionContext executionContext,
    IJobPersistenceProvider<TTimeJob, TCronJob> persistenceProvider,
    IJobsHostScheduler tickerQHostScheduler,
    IJobsNotificationHubSender notificationHubSender,
    DashboardOptionsBuilder dashboardOptions,
    IJobsDispatcher dispatcher
) : IJobsDashboardRepository<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly IJobPersistenceProvider<TTimeJob, TCronJob> _persistenceProvider = Argument.IsNotNull(
        persistenceProvider
    );
    private readonly IJobsHostScheduler _tickerQHostScheduler = Argument.IsNotNull(tickerQHostScheduler);
    private readonly IJobsDispatcher _dispatcher = Argument.IsNotNull(dispatcher);
    private readonly IJobsNotificationHubSender _notificationHubSender = Argument.IsNotNull(notificationHubSender);
    private readonly JobsExecutionContext _executionContext = Argument.IsNotNull(executionContext);
    private readonly DashboardOptionsBuilder _dashboardOptions = Argument.IsNotNull(dashboardOptions);

    public async Task<TTimeJob[]> GetTimeJobsAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceProvider.GetTimeJobs(null, cancellationToken);
    }

    public async Task<PaginationResult<TTimeJob>> GetTimeJobsPaginatedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        return await _persistenceProvider.GetTimeJobsPaginated(null, pageNumber, pageSize, cancellationToken);
    }

    public async Task<IList<Tuple<JobStatus, int>>> GetTimeJobFullDataAsync(CancellationToken cancellationToken)
    {
        var timeJobs = await _persistenceProvider.GetTimeJobs(null, cancellationToken: cancellationToken);

        var allStatuses = Enum.GetValues<JobStatus>();

        // Group by status and get counts
        var rawData = timeJobs.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToList();

        // Create a dictionary for quick lookup
        var statusCounts = rawData.ToDictionary(x => x.Status, x => x.Count);

        // Ensure all statuses are included, even those with 0 count
        var result = allStatuses
            .Select(status => new Tuple<JobStatus, int>(status, statusCounts.GetValueOrDefault(status, 0)))
            .ToList();

        return result;
    }

    public async Task<IList<JobGraphData>> GetTimeJobsGraphSpecificDataAsync(
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    )
    {
        var today = DateTime.UtcNow.Date;
        var startDate = today.AddDays(pastDays);
        var endDate = today.AddDays(futureDays);

        var timeJobs = await _persistenceProvider.GetTimeJobs(
            x =>
                (x.ExecutionTime != null)
                && (x.ExecutionTime.Value.Date >= startDate && x.ExecutionTime.Value.Date <= endDate),
            cancellationToken
        );

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
            .Range(0, (endDate - startDate).Days + 1)
            .Select(offset => startDate.AddDays(offset))
            .ToList();

        var groupedData = rawData
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Status, s => s.Count));

        var finalData = allDates
            .Select(date =>
            {
                var statusCounts = groupedData.TryGetValue(date, out var statusData)
                    ? statusData
                    : new Dictionary<JobStatus, int>();

                var results = allStatuses
                    .Select(status => new Tuple<int, int>((int)status, statusCounts.GetValueOrDefault(status, 0)))
                    .ToArray();

                return new JobGraphData { Date = date, Results = results };
            })
            .ToList();
        return finalData;
    }

    public async Task<IList<JobGraphData>> GetCronJobsGraphSpecificDataByIdAsync(
        Guid id,
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    )
    {
        var today = DateTime.UtcNow.Date;
        var startDate = today.AddDays(pastDays);
        var endDate = today.AddDays(futureDays);

        var cronJobOccurrences = await _persistenceProvider.GetAllCronJobOccurrences(
            (x => x.CronJobId == id && x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate),
            cancellationToken
        );

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
            .Range(0, (endDate - startDate).Days + 1)
            .Select(offset => startDate.AddDays(offset))
            .ToList();

        var groupedData = rawData
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Status, s => s.Count));

        var finalData = allDates
            .Select(date =>
            {
                var statusCounts = groupedData.TryGetValue(date, out var statusData)
                    ? statusData
                    : new Dictionary<JobStatus, int>();

                var results = allStatuses
                    .Select(status => new Tuple<int, int>((int)status, statusCounts.GetValueOrDefault(status, 0)))
                    .ToArray();

                return new JobGraphData { Date = date, Results = results };
            })
            .ToList();

        return finalData;
    }

    public async Task<IList<Tuple<JobStatus, int>>> GetCronJobFullDataAsync(CancellationToken cancellationToken)
    {
        var cronJobOccurrences = await _persistenceProvider.GetAllCronJobOccurrences(
            null,
            cancellationToken: cancellationToken
        );
        var allStatuses = Enum.GetValues<JobStatus>();

        var rawData = cronJobOccurrences
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToList();

        var statusCounts = rawData.ToDictionary(x => x.Status, x => x.Count);

        var result = allStatuses
            .Select(status => new Tuple<JobStatus, int>(status, statusCounts.GetValueOrDefault(status, 0)))
            .ToList();

        return result;
    }

    public async Task<IList<JobGraphData>> GetCronJobsGraphSpecificDataAsync(
        int pastDays,
        int futureDays,
        CancellationToken cancellationToken
    )
    {
        var today = DateTime.UtcNow.Date;
        var startDate = today.AddDays(pastDays);
        var endDate = today.AddDays(futureDays);

        var cronJobOccurrences = await _persistenceProvider.GetAllCronJobOccurrences(
            x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate,
            cancellationToken: cancellationToken
        );

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
            .Range(0, (endDate - startDate).Days + 1)
            .Select(offset => startDate.AddDays(offset))
            .ToList();

        var groupedData = rawData
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.Status, s => s.Count));

        var finalData = allDates
            .Select(date =>
            {
                var statusCounts = groupedData.TryGetValue(date, out var statusData)
                    ? statusData
                    : new Dictionary<JobStatus, int>();

                var results = allStatuses
                    .Select(status => new Tuple<int, int>((int)status, statusCounts.GetValueOrDefault(status, 0)))
                    .ToArray();

                return new JobGraphData { Date = date, Results = results };
            })
            .ToList();

        return finalData;
    }

    public async Task<IList<(int, int)>> GetLastWeekJobStatusesAsync(CancellationToken cancellationToken = default)
    {
        var endDate = DateTime.UtcNow.Date;
        var startDate = endDate.AddDays(-7);

        var timeJobs = await _persistenceProvider.GetTimeJobs(
            x =>
                (x.ExecutionTime != null)
                && (x.ExecutionTime.Value.Date >= startDate && x.ExecutionTime.Value.Date <= endDate),
            cancellationToken
        );

        var timeJobStatuses = timeJobs.Select(x => x.Status).ToList();

        var cronJobOccurrences = await _persistenceProvider.GetAllCronJobOccurrences(
            x => x.ExecutionTime.Date >= startDate && x.ExecutionTime.Date <= endDate,
            cancellationToken
        );

        var cronJobStatuses = cronJobOccurrences.Select(x => x.Status).ToList();

        // Merge all statuses into one list
        var allStatuses = timeJobStatuses.Concat(cronJobStatuses).ToList();

        // Count per type
        var doneOrDueDoneCount = allStatuses.Count(x => x is JobStatus.Done or JobStatus.DueDone);
        var failedCount = allStatuses.Count(x => x == JobStatus.Failed);
        var totalCount = allStatuses.Count;

        return new List<(int, int)> { (0, doneOrDueDoneCount), (1, failedCount), (2, totalCount) };
    }

    public async Task<IList<(JobStatus, int)>> GetOverallJobStatusesAsync(CancellationToken cancellationToken = default)
    {
        var timeJobs = await _persistenceProvider.GetTimeJobs(null, cancellationToken);
        var timeStatusCounts = timeJobs
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToList();

        var cronJobOccurrences = await _persistenceProvider.GetAllCronJobOccurrences(null, cancellationToken);
        var cronStatusCounts = cronJobOccurrences
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToList();

        // Combine counts into a Dictionary<JobStatus, int>
        var combined = new Dictionary<JobStatus, int>();

        foreach (var item in timeStatusCounts)
        {
            if (combined.ContainsKey(item.Status))
            {
                combined[item.Status] += item.Count;
            }
            else
            {
                combined[item.Status] = item.Count;
            }
        }

        foreach (var item in cronStatusCounts)
        {
            if (combined.ContainsKey(item.Status))
            {
                combined[item.Status] += item.Count;
            }
            else
            {
                combined[item.Status] = item.Count;
            }
        }

        // Return as list of tuples
        return combined.Select(kvp => (kvp.Key, kvp.Value)).ToList();
    }

    public async Task<IList<(string, int)>> GetMachineJobsAsync(CancellationToken cancellationToken = default)
    {
        var timeJobs = await _persistenceProvider.GetTimeJobs(x => x.LockedAt != null, cancellationToken);

        var timeJobCounts = timeJobs
            .GroupBy(x => x.LockHolder, StringComparer.Ordinal)
            .Select(g => new { LockHolder = g.Key, Count = g.Count() })
            .ToList();

        var cronJobOccurrences = await _persistenceProvider.GetAllCronJobOccurrences(
            x => x.LockedAt != null,
            cancellationToken
        );
        var cronJobCounts = cronJobOccurrences
            .GroupBy(x => x.LockHolder, StringComparer.Ordinal)
            .Select(g => new { LockHolder = g.Key, Count = g.Count() })
            .ToList();

        // Combine results into a single dictionary
        var combined = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in timeJobCounts)
        {
            if (item.LockHolder == null)
            {
                continue;
            }

            if (combined.ContainsKey(item.LockHolder))
            {
                combined[item.LockHolder] += item.Count;
            }
            else
            {
                combined[item.LockHolder] = item.Count;
            }
        }

        foreach (var item in cronJobCounts)
        {
            if (item.LockHolder == null)
            {
                continue;
            }

            if (combined.ContainsKey(item.LockHolder))
            {
                combined[item.LockHolder] += item.Count;
            }
            else
            {
                combined[item.LockHolder] = item.Count;
            }
        }

        return combined
            .Select(x => (x.Key, x.Value))
            .OrderByDescending(x => x.Value) // Optional: most active machines first
            .ToList();
    }

    public async Task<CronJobEntity[]> GetCronJobsAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceProvider.GetCronJobs(null, cancellationToken);
    }

    public async Task<PaginationResult<CronJobEntity>> GetCronJobsPaginatedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        // We need to cast TCronJob[] to CronJobEntity[] for the pagination result
        var result = await _persistenceProvider.GetCronJobsPaginated(null, pageNumber, pageSize, cancellationToken);
        return new PaginationResult<CronJobEntity>(result.Items, result.TotalCount, result.PageNumber, result.PageSize);
    }

    public async Task AddOnDemandCronJobOccurrenceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var onDemandOccurrence = new CronJobOccurrenceEntity<TCronJob>
        {
            Id = Guid.NewGuid(),
            Status = JobStatus.Idle,
            ExecutionTime = now,
            LockedAt = null,
            CronJobId = id,
        };

        await _persistenceProvider.InsertCronJobOccurrences([onDemandOccurrence], cancellationToken);

        // Acquire and run immediately
        var acquired = await _persistenceProvider
            .AcquireImmediateCronOccurrencesAsync([onDemandOccurrence.Id], cancellationToken)
            .ConfigureAwait(false);

        CronJobOccurrenceEntity<TCronJob>? acquiredOccurrence = null;

        if (acquired.Length > 0)
        {
            var occurrence = acquired[0];
            acquiredOccurrence = occurrence;
            var context = new InternalFunctionContext
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
            if (JobFunctionProvider.JobFunctions.TryGetValue(context.FunctionName, out var tickerItem))
            {
                context.CachedDelegate = tickerItem.Delegate;
                context.CachedPriority = tickerItem.Priority;
            }

            await _dispatcher.DispatchAsync([context], cancellationToken).ConfigureAwait(false);
        }

        // Notify dashboard about the new occurrence (prefer the acquired version if available)
        if (_notificationHubSender != null)
        {
            await _notificationHubSender.AddCronOccurrenceAsync(id, acquiredOccurrence ?? onDemandOccurrence);
        }
    }

    public async Task<CronJobOccurrenceEntity<TCronJob>[]> GetCronJobsOccurrencesAsync(
        Guid cronJobId,
        CancellationToken cancellationToken = default
    )
    {
        return await _persistenceProvider.GetAllCronJobOccurrences(x => x.CronJobId == cronJobId, cancellationToken);
    }

    public async Task<PaginationResult<CronJobOccurrenceEntity<TCronJob>>> GetCronJobsOccurrencesPaginatedAsync(
        Guid cronJobId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        return await _persistenceProvider.GetAllCronJobOccurrencesPaginated(
            x => x.CronJobId == cronJobId,
            pageNumber,
            pageSize,
            cancellationToken
        );
    }

    public async Task<IList<CronOccurrenceJobGraphData>> GetCronJobsOccurrencesGraphDataAsync(
        Guid guid,
        CancellationToken cancellationToken = default
    )
    {
        const int maxTotalDays = 14;
        var today = DateTime.UtcNow.Date;

        var cronJobOccurrencesPast = await _persistenceProvider.GetAllCronJobOccurrences(
            x => x.CronJobId == guid && x.ExecutionTime.Date < today,
            cancellationToken
        );
        var pastData = cronJobOccurrencesPast
            .GroupBy(x => x.ExecutionTime.Date)
            .Select(group => new CronOccurrenceJobGraphData
            {
                Date = group.Key,
                Results = group
                    .GroupBy(x => x.Status)
                    .Select(statusGroup => new Tuple<int, int>((int)statusGroup.Key, statusGroup.Count()))
                    .ToArray(),
            })
            .OrderBy(d => d.Date)
            .ToList();

        var cronJobOccurrencesToday = await _persistenceProvider.GetAllCronJobOccurrences(
            x => x.CronJobId == guid && x.ExecutionTime.Date == today,
            cancellationToken
        );

        var todayData =
            cronJobOccurrencesToday
                .GroupBy(x => x.ExecutionTime.Date)
                .Select(group => new CronOccurrenceJobGraphData
                {
                    Date = group.Key,
                    Results = group
                        .GroupBy(x => x.Status)
                        .Select(statusGroup => new Tuple<int, int>((int)statusGroup.Key, statusGroup.Count()))
                        .ToArray(),
                })
                .FirstOrDefault()
            ?? new CronOccurrenceJobGraphData { Date = today, Results = [] };

        var cronJobOccurrencesFuture = await _persistenceProvider.GetAllCronJobOccurrences(
            x => x.CronJobId == guid && x.ExecutionTime.Date > today,
            cancellationToken
        );
        var futureData = cronJobOccurrencesFuture
            .GroupBy(x => x.ExecutionTime.Date)
            .Select(group => new CronOccurrenceJobGraphData
            {
                Date = group.Key,
                Results = group
                    .GroupBy(x => x.Status)
                    .Select(statusGroup => new Tuple<int, int>((int)statusGroup.Key, statusGroup.Count()))
                    .ToArray(),
            })
            .OrderBy(d => d.Date)
            .ToList();

        int pastDaysWithData = pastData.Count;
        int futureDaysWithData = futureData.Count;

        int remainingSlots = maxTotalDays - 1; // Exclude today
        int emptyPastSlots = Math.Max(0, (remainingSlots - futureDaysWithData) / 2);
        int emptyFutureSlots = Math.Max(0, remainingSlots - pastDaysWithData - emptyPastSlots);

        List<CronOccurrenceJobGraphData> emptyPastDays = new List<CronOccurrenceJobGraphData>();
        if (emptyPastSlots > 0)
        {
            var firstPastDate = pastData.FirstOrDefault()?.Date ?? today.AddDays(-1);
            for (int i = 1; i <= emptyPastSlots; i++)
            {
                emptyPastDays.Add(new CronOccurrenceJobGraphData { Date = firstPastDate.AddDays(-i), Results = [] });
            }
        }

        List<CronOccurrenceJobGraphData> emptyFutureDays = new List<CronOccurrenceJobGraphData>();
        if (emptyFutureSlots > 0)
        {
            var lastFutureDate = futureData.LastOrDefault()?.Date ?? today.AddDays(1);
            for (int i = 1; i <= emptyFutureSlots; i++)
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

        var startDate = completeData.First().Date;
        var endDate = completeData.Last().Date;
        var allDates = Enumerable
            .Range(0, (endDate - startDate).Days + 1)
            .Select(offset => startDate.AddDays(offset))
            .ToList();

        var finalData = allDates
            .Select(date =>
                completeData.FirstOrDefault(d => d.Date == date)
                ?? new CronOccurrenceJobGraphData { Date = date, Results = [] }
            )
            .ToList();

        return finalData;
    }

    public bool CancelJobById(Guid jobId)
    {
        return JobsCancellationTokenManager.RequestTickerCancellationById(jobId);
    }

    public async Task DeleteCronJobOccurrenceByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _persistenceProvider.RemoveCronJobOccurrences([id], cancellationToken);

        if (_executionContext.Functions.Any(x => x.JobId == id))
        {
            _tickerQHostScheduler.Restart();
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
            var timeJob = await _persistenceProvider.GetTimeJobById(jobId, cancellationToken);

            if (timeJob == null)
            {
                return (string.Empty, 0);
            }

            jsonRequestBytes = timeJob.Request;
            functionName = timeJob.Function;
        }
        else
        {
            var cronJob = await _persistenceProvider.GetCronJobById(jobId, cancellationToken);

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

        if (!JobFunctionProvider.JobFunctionRequestTypes.TryGetValue(functionName, out var functionTypeContext))
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
        foreach (var jobFunction in JobFunctionProvider.JobFunctions.Select(x => new { x.Key, x.Value.Priority }))
        {
            if (JobFunctionProvider.JobFunctionRequestTypes.TryGetValue(jobFunction.Key, out var functionTypeContext))
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
