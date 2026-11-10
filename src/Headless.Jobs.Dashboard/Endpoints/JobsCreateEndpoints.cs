// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Results;
using Headless.Checks;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.Jobs;

/// <summary>
/// The Jobs module's create calls at <c>{base}/api/jobs</c>: <c>POST time-jobs</c> adds a time job or a chain, and
/// <c>POST cron-jobs</c> adds a cron job. Both need the operate policy, and both refuse bad input with the shell's
/// validation problem, naming each refused field by its path in the body.
/// </summary>
internal sealed class JobsCreateEndpoints(
    IJobsDashboardReader reader,
    IJobsDashboardWriter writer,
    TimeProvider timeProvider
)
{
    private static readonly JobsTimeJobBodyValidator _TimeJobValidator = new();
    private static readonly JobsCronJobBodyValidator _CronJobValidator = new();

    private readonly IJobsDashboardReader _reader = Argument.IsNotNull(reader);
    private readonly IJobsDashboardWriter _writer = Argument.IsNotNull(writer);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        endpoints.RequireOperate(routes.MapPost("/time-jobs", _AddTimeJobAsync));
        endpoints.RequireOperate(routes.MapPost("/cron-jobs", _AddCronJobAsync));
    }

    private async Task<Results<JsonHttpResult<JobsCreated>, ValidationProblem>> _AddTimeJobAsync(
        HttpContext context,
        CancellationToken cancellationToken
    )
    {
        var body = await _ReadAsync(context, JobsDashboardJsonContext.Default.JobsTimeJobBody, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            return DashboardValidation.Problem(
                DashboardValidation.BodyField,
                DashboardValidation.JsonRule,
                "The body must be a time job: { function, executionTime, timeZoneId, request, children }."
            );
        }

        if (
            await _TimeJobValidator.ValidateAsync(body, cancellationToken).ConfigureAwait(false) is
            { IsValid: false } invalid
        )
        {
            return DashboardValidation.Problem(invalid);
        }

        var unregistered = new List<ValidationFailure>();
        _CollectUnregistered(body, path: "", unregistered);

        if (unregistered.Count != 0)
        {
            return DashboardValidation.Problem(new ValidationResult(unregistered));
        }

        TimeZoneInfo? zone = null;

        if (
            !string.IsNullOrWhiteSpace(body.TimeZoneId)
            && !TimeZoneInfo.TryFindSystemTimeZoneById(body.TimeZoneId, out zone)
        )
        {
            return DashboardValidation.Problem(
                "timeZoneId",
                DashboardValidation.EnumRule,
                $"Unknown time zone '{body.TimeZoneId}'."
            );
        }

        zone ??= _reader.SchedulerTimeZone;
        var skipped = new List<ValidationFailure>();
        _CollectSkippedTimes(body, path: "", zone, skipped);

        if (skipped.Count != 0)
        {
            return DashboardValidation.Problem(new ValidationResult(skipped));
        }

        try
        {
            var id = await _writer.AddTimeJobAsync(_Draft(body, zone), cancellationToken).ConfigureAwait(false);

            return _Created(id);
        }
        catch (ArgumentException exception)
        {
            return _Refused(exception);
        }
    }

    private async Task<Results<JsonHttpResult<JobsCreated>, ValidationProblem>> _AddCronJobAsync(
        HttpContext context,
        CancellationToken cancellationToken
    )
    {
        var body = await _ReadAsync(context, JobsDashboardJsonContext.Default.JobsCronJobBody, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            return DashboardValidation.Problem(
                DashboardValidation.BodyField,
                DashboardValidation.JsonRule,
                "The body must be a cron job: { function, expression, timeZoneId, request }."
            );
        }

        if (
            await _CronJobValidator.ValidateAsync(body, cancellationToken).ConfigureAwait(false) is
            { IsValid: false } invalid
        )
        {
            return DashboardValidation.Problem(invalid);
        }

        // The validator refused an empty function and expression, so both are set from here on.
        var function = body.Function!;
        var expression = body.Expression!;

        if (!_IsRegistered(function))
        {
            return _Unregistered("function", function);
        }

        var zone = string.IsNullOrWhiteSpace(body.TimeZoneId) ? null : body.TimeZoneId;
        var preview = _reader.Preview(expression, zone, _Now(), count: 1);

        if (!preview.IsValid)
        {
            // A schedule that read as cron but did not resolve its zone refuses the zone; any other refusal is the
            // expression's: it does not parse, or it never fires again.
            return
                preview.Reading is not null
                && zone is not null
                && !CronTimeZoneResolver.TryResolve(zone, _reader.SchedulerTimeZone, out _)
                ? DashboardValidation.Problem("timeZoneId", DashboardValidation.EnumRule, preview.ValidationMessage!)
                : DashboardValidation.Problem("expression", DashboardValidation.FormatRule, preview.ValidationMessage!);
        }

        try
        {
            var id = await _writer
                .AddCronJobAsync(
                    new JobsDashboardCronJobDraft
                    {
                        Function = function,
                        Expression = expression,
                        TimeZoneId = zone,
                        RequestJson = _Json(body.Request),
                        Description = body.Description,
                        Retries = body.Retries,
                        RetryIntervals = body.RetryIntervals,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            return _Created(id);
        }
        catch (ArgumentException exception)
        {
            return _Refused(exception);
        }
    }

    private void _CollectUnregistered(JobsTimeJobBody body, string path, List<ValidationFailure> failures)
    {
        if (!_IsRegistered(body.Function!))
        {
            failures.Add(
                new ValidationFailure(path + "function", _UnregisteredMessage(body.Function!))
                {
                    ErrorCode = DashboardValidation.EnumRule,
                }
            );
        }

        var children = body.Children ?? [];

        for (var index = 0; index < children.Count; index++)
        {
            _CollectUnregistered(
                children[index],
                string.Create(CultureInfo.InvariantCulture, $"{path}children[{index}]."),
                failures
            );
        }
    }

    // Converting a wall-clock time a daylight-saving gap skips throws, which would refuse the step's request instead of
    // the time the operator picked; so each step's time is checked first and the refusal names that step's field.
    private static void _CollectSkippedTimes(
        JobsTimeJobBody body,
        string path,
        TimeZoneInfo zone,
        List<ValidationFailure> failures
    )
    {
        if (body.ExecutionTime is { Kind: DateTimeKind.Unspecified } at && zone.IsInvalidTime(at))
        {
            failures.Add(
                new ValidationFailure(
                    path + "executionTime",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{at:yyyy-MM-dd HH:mm} does not exist in {zone.Id}: the clocks skip it for daylight saving. Choose a time outside the gap."
                    )
                )
                {
                    ErrorCode = DashboardValidation.InvalidRule,
                }
            );
        }

        var children = body.Children ?? [];

        for (var index = 0; index < children.Count; index++)
        {
            _CollectSkippedTimes(
                children[index],
                string.Create(CultureInfo.InvariantCulture, $"{path}children[{index}]."),
                zone,
                failures
            );
        }
    }

    private bool _IsRegistered(string function) =>
        _reader.Functions.Any(registered => string.Equals(registered.Name, function, StringComparison.Ordinal));

    private static ValidationProblem _Unregistered(string field, string function) =>
        DashboardValidation.Problem(field, DashboardValidation.EnumRule, _UnregisteredMessage(function));

    private static string _UnregisteredMessage(string function) => $"No function named '{function}' is registered.";

    private static JsonHttpResult<JobsCreated> _Created(Guid id) =>
        TypedResults.Json(
            new JobsCreated(id),
            JobsDashboardJsonContext.Default.JobsCreated,
            statusCode: StatusCodes.Status201Created
        );

    // The backend refuses a request its function cannot bind, or a function it does not register; the request it
    // names may be any step's, so the refusal names the request field and its message names the function.
    private static ValidationProblem _Refused(ArgumentException exception) =>
        DashboardValidation.Problem(
            string.Equals(exception.ParamName, "function", StringComparison.Ordinal) ? "function" : "request",
            DashboardValidation.InvalidRule,
            _Reason(exception)
        );

    /// <summary>Turns a validated create body into a draft, converting wall-clock times in <paramref name="zone"/> to UTC.</summary>
    private static JobsDashboardTimeJobDraft _Draft(JobsTimeJobBody body, TimeZoneInfo zone)
    {
        return new JobsDashboardTimeJobDraft
        {
            Function = body.Function!,
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
