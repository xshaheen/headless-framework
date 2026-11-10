// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Globalization;
using Headless.Checks;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.Jobs;

/// <summary>
/// The Jobs module's own read API at <c>{base}/api/jobs</c>: runs, attempts, cron jobs with their next fires, a draft
/// schedule preview, functions, nodes, and runs per day. <see cref="JobsCreateEndpoints"/> maps the create calls.
/// </summary>
internal sealed class JobsModuleApi(
    IJobsDashboardReader reader,
    JobsDashboardEventNotifier notifier,
    TimeProvider timeProvider
)
{
    /// <summary>The most runs one list call returns.</summary>
    internal const int MaxListLimit = 200;

    /// <summary>The most next fires one preview returns.</summary>
    internal const int MaxFires = 50;

    /// <summary>The most days one series covers.</summary>
    internal const int MaxSeriesDays = 92;

    private static readonly DateOnly _FirstSeriesDay = DateOnly.MinValue.AddDays(1);
    private static readonly DateOnly _LastSeriesDay = DateOnly.MaxValue.AddDays(-1);

    private const int _DefaultListLimit = 50;
    private const int _DefaultFires = 5;

    private readonly IJobsDashboardReader _reader = Argument.IsNotNull(reader);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private readonly JobsDashboardEventNotifier _notifier = Argument.IsNotNull(notifier);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        routes.MapGet("/runs", _ListRunsAsync);
        routes.MapGet("/runs/{runId}", _GetRunAsync);
        routes.MapGet("/runs/{runId}/attempts", _GetAttemptsAsync);
        routes.MapGet("/cron-jobs", _ListCronJobsAsync);
        routes.MapGet("/cron-jobs/{cronJobId}", _GetCronJobAsync);
        routes.MapGet("/cron-preview", _Preview);
        routes.MapGet("/functions", _ListFunctionsAsync);
        routes.MapGet("/nodes", _ListNodesAsync);
        routes.MapGet("/series/daily", _DailySeriesAsync);
    }

    private async Task<Results<JsonHttpResult<JobsRunList>, BadRequest<string>>> _ListRunsAsync(
        string? kind,
        string[]? function,
        string[]? status,
        Guid? cronJobId,
        string? correlationId,
        string? fingerprint,
        DateTime? from,
        DateTime? before,
        bool? actionable,
        string? search,
        string? sort,
        string? direction,
        int? limit,
        string? after,
        CancellationToken cancellationToken
    )
    {
        JobRunKind? runKind = null;

        if (!string.IsNullOrEmpty(kind))
        {
            if (!Enum.TryParse<JobRunKind>(kind, ignoreCase: true, out var parsedKind) || !Enum.IsDefined(parsedKind))
            {
                return TypedResults.BadRequest("kind must be TimeJob or CronJobOccurrence.");
            }

            runKind = parsedKind;
        }

        var statuses = new List<JobStatus>();

        foreach (var value in status ?? [])
        {
            if (!Enum.TryParse<JobStatus>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return TypedResults.BadRequest($"'{value}' is not a run status.");
            }

            statuses.Add(parsed);
        }

        var sortBy = JobRunSortKey.ExecutionTime;

        if (!string.IsNullOrEmpty(sort))
        {
            if (!Enum.TryParse(sort, ignoreCase: true, out sortBy) || !Enum.IsDefined(sortBy))
            {
                return TypedResults.BadRequest("sort must be executionTime, function, or status.");
            }
        }

        // The list opens on the latest runs, so the execution time reads descending unless asked otherwise; a text
        // sort key reads A to Z.
        var sortDirection =
            sortBy == JobRunSortKey.ExecutionTime ? ListSortDirection.Descending : ListSortDirection.Ascending;

        if (!string.IsNullOrEmpty(direction))
        {
            if (!Enum.TryParse(direction, ignoreCase: true, out sortDirection) || !Enum.IsDefined(sortDirection))
            {
                return TypedResults.BadRequest("direction must be ascending or descending.");
            }
        }

        JobRunCursor? cursor = null;

        if (!string.IsNullOrEmpty(after))
        {
            if (!JobRunCursor.TryParse(after, out cursor))
            {
                return TypedResults.BadRequest("after is not a cursor this list returned.");
            }
        }

        var query = new JobsRunScope
        {
            Kind = runKind,
            Functions = function,
            Statuses = statuses,
            CronJobId = cronJobId,
            CorrelationId = correlationId,
            Fingerprint = fingerprint,
            From = from,
            Before = before,
            ActionableOnly = actionable == true,
            Search = search,
        }.ToQuery() with
        {
            SortBy = sortBy,
            SortDirection = sortDirection,
        };

        if (cursor?.Fits(query) == false)
        {
            return TypedResults.BadRequest(
                "after was read with another sort, direction, filter, or search; read the first page again."
            );
        }

        var take = Math.Clamp(limit ?? _DefaultListLimit, 1, MaxListLimit);
        var page = await _reader.ListRunsAsync(query, cursor, take, cancellationToken).ConfigureAwait(false);
        var count = await _reader.CountRunsAsync(query, cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(
            new JobsRunList([.. page.Runs.Select(JobsRunView.From)], count.Count, count.Capped, page.Next?.ToString()),
            JobsDashboardJsonContext.Default.JobsRunList
        );
    }

    private async Task<Results<JsonHttpResult<JobsRunDetail>, NotFound>> _GetRunAsync(
        string runId,
        CancellationToken cancellationToken
    )
    {
        if (
            !Guid.TryParse(runId, out var id)
            || await _reader.GetRunAsync(id, cancellationToken).ConfigureAwait(false) is not { } run
        )
        {
            return TypedResults.NotFound();
        }

        var children =
            run.Kind == JobRunKind.TimeJob
                ? await _reader.GetChildIdsAsync(run.Id, cancellationToken).ConfigureAwait(false)
                : [];

        return TypedResults.Json(
            new JobsRunDetail(JobsRunView.From(run), children),
            JobsDashboardJsonContext.Default.JobsRunDetail
        );
    }

    private async Task<Results<JsonHttpResult<JobsAttemptList>, NotFound>> _GetAttemptsAsync(
        string runId,
        CancellationToken cancellationToken
    )
    {
        if (
            !Guid.TryParse(runId, out var id)
            || await _reader.GetRunAsync(id, cancellationToken).ConfigureAwait(false) is not { } run
        )
        {
            return TypedResults.NotFound();
        }

        var attempts = await _reader.GetAttemptsAsync(run.Kind, run.Id, cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(
            new JobsAttemptList([.. attempts.OrderBy(static a => a.Number).Select(JobsAttemptView.From)]),
            JobsDashboardJsonContext.Default.JobsAttemptList
        );
    }

    private async Task<JsonHttpResult<JobsCronJobList>> _ListCronJobsAsync(
        string? function,
        int? fires,
        CancellationToken cancellationToken
    )
    {
        // Computing next fires costs a cron parse per job, so a list caller gets them only when it asks for them.
        var count = fires is null ? 1 : Math.Clamp(fires.Value, 1, MaxFires);
        var cronJobs = await _reader.GetCronJobsAsync(cancellationToken).ConfigureAwait(false);
        var now = _Now();

        return TypedResults.Json(
            new JobsCronJobList([
                .. cronJobs
                    .Where(cronJob =>
                        function is null || string.Equals(cronJob.Function, function, StringComparison.Ordinal)
                    )
                    .OrderBy(static cronJob => cronJob.Function, StringComparer.Ordinal)
                    .ThenBy(static cronJob => cronJob.Id)
                    .Select(cronJob =>
                    {
                        var preview = _reader.Preview(cronJob.Expression, cronJob.TimeZoneId, now, count);

                        return JobsCronJobView.From(
                            cronJob,
                            preview,
                            fires is null || cronJob.IsPaused ? [] : preview.NextFiresUtc
                        );
                    }),
            ]),
            JobsDashboardJsonContext.Default.JobsCronJobList
        );
    }

    private async Task<Results<JsonHttpResult<JobsCronJobView>, NotFound>> _GetCronJobAsync(
        string cronJobId,
        int? fires,
        CancellationToken cancellationToken
    )
    {
        if (
            !Guid.TryParse(cronJobId, out var id)
            || await _reader.GetCronJobAsync(id, cancellationToken).ConfigureAwait(false) is not { } cronJob
        )
        {
            return TypedResults.NotFound();
        }

        // A paused cron job fires nothing until it is resumed, so it lists no next fires.
        var preview = _reader.Preview(
            cronJob.Expression,
            cronJob.TimeZoneId,
            _Now(),
            Math.Clamp(fires ?? _DefaultFires, 1, MaxFires)
        );

        return TypedResults.Json(
            JobsCronJobView.From(cronJob, preview, cronJob.IsPaused ? [] : preview.NextFiresUtc),
            JobsDashboardJsonContext.Default.JobsCronJobView
        );
    }

    private Results<JsonHttpResult<JobsSchedulePreview>, BadRequest<string>> _Preview(
        string? expression,
        string? timeZoneId,
        int? count
    )
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return TypedResults.BadRequest("expression is required.");
        }

        var zone = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId;
        var preview = _reader.Preview(expression, zone, _Now(), Math.Clamp(count ?? _DefaultFires, 1, MaxFires));

        return TypedResults.Json(
            new JobsSchedulePreview(
                zone ?? _reader.SchedulerTimeZone.Id,
                preview.Reading,
                preview.NextFiresUtc,
                preview.ValidationMessage
            ),
            JobsDashboardJsonContext.Default.JobsSchedulePreview
        );
    }

    private async Task<JsonHttpResult<JobsFunctionList>> _ListFunctionsAsync(CancellationToken cancellationToken)
    {
        var since = _timeProvider.GetUtcNow() - JobOverviewFigures.Window;
        var stats = (await _reader.GetFunctionStatsAsync(since, cancellationToken).ConfigureAwait(false)).ToDictionary(
            static row => row.Function,
            StringComparer.Ordinal
        );
        var nodes = await _reader.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        var cronJobs = await _reader.GetCronJobsAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(
            new JobsFunctionList([
                .. _reader.Functions.Select(function =>
                {
                    var row = stats.GetValueOrDefault(function.Name);

                    return new JobsFunctionView(
                        function.Name,
                        function.ContractVersion,
                        function.Priority,
                        function.MaxConcurrency,
                        function.RequestType,
                        function.CronExpression,
                        [
                            .. nodes
                                .Where(node => node.ClaimedFunctions?.Contains(function.Name) == true)
                                .Select(static node => node.Identity),
                        ],
                        row?.Succeeded ?? 0,
                        row?.Failed ?? 0,
                        row?.Running ?? 0,
                        row?.Waiting ?? 0,
                        row?.LastExecutedAt,
                        [
                            .. cronJobs
                                .Where(cronJob =>
                                    string.Equals(cronJob.Function, function.Name, StringComparison.Ordinal)
                                )
                                .Select(static cronJob => cronJob.Id),
                        ]
                    );
                }),
            ]),
            JobsDashboardJsonContext.Default.JobsFunctionList
        );
    }

    private async Task<JsonHttpResult<JobsNodeList>> _ListNodesAsync(CancellationToken cancellationToken)
    {
        var nodes = await _reader.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        var running = await _reader.CountRunningByOwnerAsync(cancellationToken).ConfigureAwait(false);
        var hostException = _notifier.LastHostException;

        return TypedResults.Json(
            new JobsNodeList([
                .. nodes.Select(node =>
                {
                    var count = running.GetValueOrDefault(node.Owner);
                    // Only this host's exception is known here; a peer keeps its own in its own process.
                    var exception = node.IsLocal ? hostException : null;

                    return new JobsNodeView(
                        node.Identity,
                        node.State,
                        node.Role,
                        node.ClaimedFunctions,
                        node.IsLocal,
                        node.MaxConcurrency,
                        count.Count,
                        count.Capped,
                        node.LastHeartbeatAt,
                        exception?.Message,
                        exception?.At
                    );
                }),
            ]),
            JobsDashboardJsonContext.Default.JobsNodeList
        );
    }

    private DateTime _Now() => _timeProvider.GetUtcNow().UtcDateTime;

    private async Task<Results<JsonHttpResult<JobsDailySeries>, BadRequest<string>>> _DailySeriesAsync(
        string? kind,
        Guid? cronJobId,
        DateOnly? from,
        DateOnly? to,
        string? zone,
        CancellationToken cancellationToken
    )
    {
        JobRunKind? runKind = null;

        if (!string.IsNullOrEmpty(kind))
        {
            if (!Enum.TryParse<JobRunKind>(kind, ignoreCase: true, out var parsedKind) || !Enum.IsDefined(parsedKind))
            {
                return TypedResults.BadRequest("kind must be TimeJob or CronJobOccurrence.");
            }

            runKind = parsedKind;
        }

        if (from is not { } first || to is not { } last)
        {
            return TypedResults.BadRequest("from and to are required local dates (yyyy-MM-dd).");
        }

        if (last < first)
        {
            return TypedResults.BadRequest("to must not be before from.");
        }

        // The edges run from the first day's midnight to the day after the last, converted to UTC, so a day at either
        // end of the calendar has no edge to stand on.
        if (first < _FirstSeriesDay || last > _LastSeriesDay)
        {
            return TypedResults.BadRequest(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"from and to must be between {_FirstSeriesDay:yyyy-MM-dd} and {_LastSeriesDay:yyyy-MM-dd}."
                )
            );
        }

        if (last.DayNumber - first.DayNumber + 1 > MaxSeriesDays)
        {
            return TypedResults.BadRequest($"A series covers at most {MaxSeriesDays} days.");
        }

        var timeZone = _reader.SchedulerTimeZone;

        if (!string.IsNullOrWhiteSpace(zone) && !TimeZoneInfo.TryFindSystemTimeZoneById(zone, out timeZone))
        {
            return TypedResults.BadRequest($"Unknown time zone '{zone}'.");
        }

        var days = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1).Select(first.AddDays).ToArray();
        DateTime[] edges =
        [
            .. days.Select(day => _DayStartUtc(day, timeZone)),
            _DayStartUtc(last.AddDays(1), timeZone),
        ];
        var asOf = _timeProvider.GetUtcNow();
        var buckets = await _reader
            .CountRunsByBucketAsync(new JobRunQuery { Kind = runKind, CronJobId = cronJobId }, edges, cancellationToken)
            .ConfigureAwait(false);
        var statuses = Enum.GetValues<JobStatus>();

        return TypedResults.Json(
            new JobsDailySeries(
                timeZone.Id,
                first,
                last,
                asOf,
                buckets.All(static bucket => bucket.Counts is not null),
                [
                    .. buckets.Select(
                        (bucket, index) =>
                            new JobsDailyBucket(
                                days[index],
                                bucket.Start,
                                bucket.End,
                                bucket.Counts is { } counts
                                    ? statuses.ToDictionary(
                                        static status => status.ToString(),
                                        status => counts.GetValueOrDefault(status),
                                        StringComparer.Ordinal
                                    )
                                    : null
                            )
                    ),
                ]
            ),
            JobsDashboardJsonContext.Default.JobsDailySeries
        );
    }

    /// <summary>
    /// The first instant of a local day: its midnight, or, where a daylight-saving gap skips midnight, the end of the
    /// gap, and the earlier instant where midnight repeats. So each day starts once and a short or long day keeps its
    /// true length.
    /// </summary>
    private static DateTime _DayStartUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // Gaps begin and end on whole minutes in the time-zone database, so the first valid minute is the gap's end.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        if (zone.IsAmbiguousTime(local))
        {
            return DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc);
        }

        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, zone), DateTimeKind.Utc);
    }
}
