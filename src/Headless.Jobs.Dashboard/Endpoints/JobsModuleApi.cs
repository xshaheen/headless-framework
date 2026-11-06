// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.Jobs;

/// <summary>
/// The Jobs module's own API at <c>{base}/api/jobs</c>: runs, attempts, cron jobs with their next fires, a draft
/// schedule preview, functions, nodes, and the two create calls, which need the operate policy.
/// </summary>
internal sealed class JobsModuleApi(IJobsDashboardBackend backend, TimeProvider timeProvider)
{
    /// <summary>The most runs one list call returns.</summary>
    internal const int MaxListLimit = 200;

    /// <summary>The most next fires one preview returns.</summary>
    internal const int MaxFires = 50;

    private const int _DefaultListLimit = 50;
    private const int _DefaultFires = 5;

    private readonly IJobsDashboardBackend _backend = Argument.IsNotNull(backend);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

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
        endpoints.RequireOperate(routes.MapPost("/time-jobs", _AddTimeJobAsync));
        endpoints.RequireOperate(routes.MapPost("/cron-jobs", _AddCronJobAsync));
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
        bool? newest,
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
        }.ToQuery() with
        {
            NewestFirst = newest != false,
        };

        var take = Math.Clamp(limit ?? _DefaultListLimit, 1, MaxListLimit);
        var page = await _backend.ListRunsAsync(query, cursor, take, cancellationToken).ConfigureAwait(false);
        var count = await _backend.CountRunsAsync(query, cancellationToken).ConfigureAwait(false);

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
            || await _backend.GetRunAsync(id, cancellationToken).ConfigureAwait(false) is not { } run
        )
        {
            return TypedResults.NotFound();
        }

        var children =
            run.Kind == JobRunKind.TimeJob
                ? await _backend.GetChildIdsAsync(run.Id, cancellationToken).ConfigureAwait(false)
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
            || await _backend.GetRunAsync(id, cancellationToken).ConfigureAwait(false) is not { } run
        )
        {
            return TypedResults.NotFound();
        }

        var attempts = await _backend.GetAttemptsAsync(run.Kind, run.Id, cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(
            new JobsAttemptList([.. attempts.OrderBy(static a => a.Number).Select(JobsAttemptView.From)]),
            JobsDashboardJsonContext.Default.JobsAttemptList
        );
    }

    private async Task<JsonHttpResult<JobsCronJobList>> _ListCronJobsAsync(
        string? function,
        CancellationToken cancellationToken
    )
    {
        var cronJobs = await _backend.GetCronJobsAsync(cancellationToken).ConfigureAwait(false);
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
                        JobsCronJobView.From(
                            cronJob,
                            _backend.Preview(cronJob.Expression, cronJob.TimeZoneId, now, count: 1),
                            nextFires: []
                        )
                    ),
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
            || await _backend.GetCronJobAsync(id, cancellationToken).ConfigureAwait(false) is not { } cronJob
        )
        {
            return TypedResults.NotFound();
        }

        // A paused cron job fires nothing until it is resumed, so it lists no next fires.
        var preview = _backend.Preview(
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
        var preview = _backend.Preview(expression, zone, _Now(), Math.Clamp(count ?? _DefaultFires, 1, MaxFires));

        return TypedResults.Json(
            new JobsSchedulePreview(
                zone ?? _backend.SchedulerTimeZone.Id,
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
        var stats = (await _backend.GetFunctionStatsAsync(since, cancellationToken).ConfigureAwait(false)).ToDictionary(
            static row => row.Function,
            StringComparer.Ordinal
        );
        var nodes = await _backend.GetNodesAsync(cancellationToken).ConfigureAwait(false);
        var cronJobs = await _backend.GetCronJobsAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(
            new JobsFunctionList([
                .. _backend.Functions.Select(function =>
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
        var nodes = await _backend.GetNodesAsync(cancellationToken).ConfigureAwait(false);

        return TypedResults.Json(
            new JobsNodeList([
                .. nodes.Select(static node => new JobsNodeView(
                    node.Identity,
                    node.State,
                    node.Role,
                    node.ClaimedFunctions
                )),
            ]),
            JobsDashboardJsonContext.Default.JobsNodeList
        );
    }

    private async Task<Results<JsonHttpResult<JobsCreated>, ProblemHttpResult>> _AddTimeJobAsync(
        HttpContext context,
        CancellationToken cancellationToken
    )
    {
        var body = await _ReadAsync(context, JobsDashboardJsonContext.Default.JobsTimeJobBody, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            return _Invalid("The body must be a time job: { function, executionTime, timeZoneId, request, children }.");
        }

        TimeZoneInfo? zone = null;

        if (
            !string.IsNullOrWhiteSpace(body.TimeZoneId)
            && !TimeZoneInfo.TryFindSystemTimeZoneById(body.TimeZoneId, out zone)
        )
        {
            return _Invalid($"Unknown time zone '{body.TimeZoneId}'.");
        }

        try
        {
            var id = await _backend
                .AddTimeJobAsync(_Draft(body, zone ?? _backend.SchedulerTimeZone), cancellationToken)
                .ConfigureAwait(false);

            return TypedResults.Json(
                new JobsCreated(id),
                JobsDashboardJsonContext.Default.JobsCreated,
                statusCode: StatusCodes.Status201Created
            );
        }
        catch (ArgumentException exception)
        {
            return _Invalid(_Reason(exception));
        }
    }

    private async Task<Results<JsonHttpResult<JobsCreated>, ProblemHttpResult>> _AddCronJobAsync(
        HttpContext context,
        CancellationToken cancellationToken
    )
    {
        var body = await _ReadAsync(context, JobsDashboardJsonContext.Default.JobsCronJobBody, cancellationToken)
            .ConfigureAwait(false);

        if (body is null || string.IsNullOrWhiteSpace(body.Function) || string.IsNullOrWhiteSpace(body.Expression))
        {
            return _Invalid("The body must be a cron job: { function, expression, timeZoneId, request }.");
        }

        var zone = string.IsNullOrWhiteSpace(body.TimeZoneId) ? null : body.TimeZoneId;
        var preview = _backend.Preview(body.Expression, zone, _Now(), count: 1);

        if (!preview.IsValid)
        {
            return _Invalid(preview.ValidationMessage!);
        }

        try
        {
            var id = await _backend
                .AddCronJobAsync(
                    new JobsDashboardCronJobDraft
                    {
                        Function = body.Function,
                        Expression = body.Expression,
                        TimeZoneId = zone,
                        RequestJson = _Json(body.Request),
                        Description = body.Description,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            return TypedResults.Json(
                new JobsCreated(id),
                JobsDashboardJsonContext.Default.JobsCreated,
                statusCode: StatusCodes.Status201Created
            );
        }
        catch (ArgumentException exception)
        {
            return _Invalid(_Reason(exception));
        }
    }

    /// <summary>Turns a create body into a draft, converting wall-clock times in <paramref name="zone"/> to UTC.</summary>
    /// <exception cref="ArgumentException">A step names no function.</exception>
    private static JobsDashboardTimeJobDraft _Draft(JobsTimeJobBody body, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(body.Function))
        {
            throw new ArgumentException("Every time job and chain step needs a function.", nameof(body));
        }

        return new JobsDashboardTimeJobDraft
        {
            Function = body.Function,
            ExecutionTime = body.ExecutionTime is { } at ? _ToUtc(at, zone) : null,
            RequestJson = _Json(body.Request),
            Description = body.Description,
            Retries = body.Retries,
            RetryIntervals = body.RetryIntervals,
            RunCondition = body.RunCondition,
            Children = [.. (body.Children ?? []).Select(child => _Draft(child, zone))],
        };
    }

    // A time without an offset is a wall-clock time in the zone the form names; one with Z or an offset is an instant.
    private static DateTime _ToUtc(DateTime value, TimeZoneInfo zone) =>
        value.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeToUtc(value, zone),
                DateTimeKind.Utc
            ),
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => value,
        };

    private static string? _Json(JsonElement? request) =>
        request is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value ? value.GetRawText() : null;

    private static async Task<T?> _ReadAsync<T>(
        HttpContext context,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken
    )
        where T : class
    {
        try
        {
            return await JsonSerializer
                .DeserializeAsync(context.Request.Body, typeInfo, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProblemHttpResult _Invalid(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// The reason an argument was refused, without the <c> (Parameter 'json')</c> suffix .NET adds for a developer:
    /// the form shows it to an operator, who never named that parameter.
    /// </summary>
    private static string _Reason(ArgumentException exception)
    {
        var suffix = $" (Parameter '{exception.ParamName}')";

        return exception.ParamName is not null && exception.Message.EndsWith(suffix, StringComparison.Ordinal)
            ? exception.Message[..^suffix.Length]
            : exception.Message;
    }

    private DateTime _Now() => _timeProvider.GetUtcNow().UtcDateTime;
}
