// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.Jobs;

/// <summary>
/// The Jobs module's create calls at <c>{base}/api/jobs</c>: <c>POST time-jobs</c> adds a time job or a chain, and
/// <c>POST cron-jobs</c> adds a cron job. Both need the operate policy.
/// </summary>
internal sealed class JobsCreateEndpoints(
    IJobsDashboardReader reader,
    IJobsDashboardWriter writer,
    TimeProvider timeProvider
)
{
    private readonly IJobsDashboardReader _reader = Argument.IsNotNull(reader);
    private readonly IJobsDashboardWriter _writer = Argument.IsNotNull(writer);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        endpoints.RequireOperate(routes.MapPost("/time-jobs", _AddTimeJobAsync));
        endpoints.RequireOperate(routes.MapPost("/cron-jobs", _AddCronJobAsync));
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
            var id = await _writer
                .AddTimeJobAsync(_Draft(body, zone ?? _reader.SchedulerTimeZone), cancellationToken)
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
        var preview = _reader.Preview(body.Expression, zone, _Now(), count: 1);

        if (!preview.IsValid)
        {
            return _Invalid(preview.ValidationMessage!);
        }

        try
        {
            var id = await _writer
                .AddCronJobAsync(
                    new JobsDashboardCronJobDraft
                    {
                        Function = body.Function,
                        Expression = body.Expression,
                        TimeZoneId = zone,
                        RequestJson = _Json(body.Request),
                        Description = body.Description,
                        Retries = body.Retries,
                        RetryIntervals = body.RetryIntervals,
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
