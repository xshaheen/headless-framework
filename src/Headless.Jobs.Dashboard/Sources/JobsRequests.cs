// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using FluentValidation;
using FluentValidation.Results;
using Headless.Dashboard;

namespace Headless.Jobs;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
/// <summary>The body of <c>POST time-jobs</c>: a time job, or a chain when it has children.</summary>
internal sealed record JobsTimeJobBody
{
    public string? Function { get; init; }

    /// <summary>The due time. Without an offset it is a wall-clock time in <see cref="TimeZoneId"/>, else UTC.</summary>
    public DateTime? ExecutionTime { get; init; }

    public string? TimeZoneId { get; init; }

    public JsonElement? Request { get; init; }

    public string? Description { get; init; }

    public int Retries { get; init; }

    public int[]? RetryIntervals { get; init; }

    public RunCondition? RunCondition { get; init; }

    public IReadOnlyList<JobsTimeJobBody>? Children { get; init; }
}

/// <summary>
/// The checks a time job body passes before any store read: each step names a function, its retries and intervals are
/// in range, and its request fits. Whether the function is registered and the zone is known are checked against the
/// host after this.
/// </summary>
internal sealed class JobsTimeJobBodyValidator : AbstractValidator<JobsTimeJobBody>
{
    public JobsTimeJobBodyValidator()
    {
        RuleFor(body => body.Function).NotEmpty().WithMessage("Choose a function.");
        RuleFor(body => body.Retries).JobsRetries();
        RuleForEach(body => body.RetryIntervals).JobsRetryInterval();
        RuleFor(body => body.Request).JobsRequest();
        // A chain step is a time job of its own, so its fields are named by their path: children[0].function.
        RuleForEach(body => body.Children)
            .NotNull()
            .WithMessage("Each chain step must be a time job.")
            .SetValidator(this);
    }
}

/// <summary>The body of <c>POST cron-jobs</c>.</summary>
internal sealed record JobsCronJobBody
{
    public string? Function { get; init; }

    public string? Expression { get; init; }

    public string? TimeZoneId { get; init; }

    public JsonElement? Request { get; init; }

    public string? Description { get; init; }

    public int Retries { get; init; }

    public int[]? RetryIntervals { get; init; }
}

/// <summary>
/// The checks a cron job body passes before any store read. Whether the expression parses, the function is registered,
/// and the zone is known are checked against the host after this.
/// </summary>
internal sealed class JobsCronJobBodyValidator : AbstractValidator<JobsCronJobBody>
{
    public JobsCronJobBodyValidator()
    {
        RuleFor(body => body.Function).NotEmpty().WithMessage("Choose a function.");
        RuleFor(body => body.Expression).NotEmpty().WithMessage("Enter a cron expression.");
        RuleFor(body => body.Retries).JobsRetries();
        RuleForEach(body => body.RetryIntervals).JobsRetryInterval();
        RuleFor(body => body.Request).JobsRequest();
    }
}

/// <summary>The query of <c>GET runs</c>, as the Runs list sends it.</summary>
/// <param name="After">The cursor of the page before; without one the list reads its first page and counts.</param>
internal sealed record JobsRunListQuery(
    string? Kind,
    string[]? Function,
    string[]? Status,
    Guid? CronJobId,
    string? CorrelationId,
    string? Fingerprint,
    DateTime? From,
    DateTime? Before,
    bool? Actionable,
    string? Search,
    string? Sort,
    string? Direction,
    int? Limit,
    string? After
)
{
    /// <summary>The run kind asked for, or <see langword="null"/> for every kind. Read after validation.</summary>
    public JobRunKind? ReadKind() => DashboardValidation.ParseEnum<JobRunKind>(Kind);

    /// <summary>The statuses asked for. Read after validation.</summary>
    public IReadOnlyList<JobStatus> ReadStatuses() =>
        [.. (Status ?? []).Select(static value => DashboardValidation.ParseEnum<JobStatus>(value)!.Value)];

    /// <summary>The sort key, the execution time unless asked otherwise. Read after validation.</summary>
    public JobRunSortKey ReadSort() =>
        DashboardValidation.ParseEnum<JobRunSortKey>(Sort) ?? JobRunSortKey.ExecutionTime;

    /// <summary>
    /// The direction. The list opens on the latest runs, so the execution time reads descending unless asked otherwise;
    /// a text sort key reads A to Z. Read after validation.
    /// </summary>
    public ListSortDirection ReadDirection() =>
        DashboardValidation.ParseEnum<ListSortDirection>(Direction)
        ?? (ReadSort() == JobRunSortKey.ExecutionTime ? ListSortDirection.Descending : ListSortDirection.Ascending);

    /// <summary>The cursor of the page before, or <see langword="null"/> for the first page. Read after validation.</summary>
    public JobRunCursor? ReadCursor() => JobRunCursor.TryParse(After, out var cursor) ? cursor : null;
}

/// <summary>The checks a <c>GET runs</c> query passes before the list is read.</summary>
internal sealed class JobsRunListQueryValidator : AbstractValidator<JobsRunListQuery>
{
    public JobsRunListQueryValidator()
    {
        RuleFor(query => query.Kind)
            .Must(JobsRequestRules.IsEnumOrEmpty<JobRunKind>)
            .WithErrorCode(DashboardValidation.EnumRule)
            .WithMessage("kind must be TimeJob or CronJobOccurrence.");
        // Statuses repeat as status=Failed&status=Lost, with no index, so the refusal names the field and the value.
        RuleFor(query => query.Status)
            .Custom(
                static (statuses, context) =>
                {
                    foreach (var value in statuses ?? [])
                    {
                        if (DashboardValidation.ParseEnum<JobStatus>(value) is null)
                        {
                            context.AddFailure(
                                new ValidationFailure(context.PropertyPath, $"'{value}' is not a run status.")
                                {
                                    ErrorCode = DashboardValidation.EnumRule,
                                }
                            );
                        }
                    }
                }
            );
        RuleFor(query => query.Sort)
            .Must(JobsRequestRules.IsEnumOrEmpty<JobRunSortKey>)
            .WithErrorCode(DashboardValidation.EnumRule)
            .WithMessage("sort must be executionTime, function, or status.");
        RuleFor(query => query.Direction)
            .Must(JobsRequestRules.IsEnumOrEmpty<ListSortDirection>)
            .WithErrorCode(DashboardValidation.EnumRule)
            .WithMessage("direction must be ascending or descending.");
        RuleFor(query => query.After)
            .Must(static after => string.IsNullOrEmpty(after) || JobRunCursor.TryParse(after, out _))
            .WithErrorCode(DashboardValidation.FormatRule)
            .WithMessage("after is not a cursor this list returned.");
    }
}

/// <summary>
/// The filter an Operation is scoped by, in the shape the Runs list sends it and a Needs-you group hands it over.
/// </summary>
internal sealed record JobsRunScope
{
    public JobRunKind? Kind { get; init; }

    public IReadOnlyList<string>? Functions { get; init; }

    public IReadOnlyList<JobStatus>? Statuses { get; init; }

    public Guid? CronJobId { get; init; }

    public string? CorrelationId { get; init; }

    public string? Fingerprint { get; init; }

    public DateTime? From { get; init; }

    public DateTime? Before { get; init; }

    /// <summary>Whether only actionable failures are in scope, as a Needs-you group's filter asks.</summary>
    public bool ActionableOnly { get; init; }

    /// <summary>The Runs list's search text, so an Operation covers the rows the operator sees and no more.</summary>
    public string? Search { get; init; }

    public JobRunQuery ToQuery() =>
        new()
        {
            Kind = Kind,
            Functions = Functions is { Count: > 0 } functions ? [.. functions] : null,
            Statuses = Statuses is { Count: > 0 } statuses ? [.. statuses] : null,
            CronJobId = CronJobId,
            CorrelationId = string.IsNullOrEmpty(CorrelationId) ? null : CorrelationId,
            FailureFingerprint = string.IsNullOrEmpty(Fingerprint) ? null : Fingerprint,
            ExecutionFrom = From is { } from ? DateTime.SpecifyKind(from, DateTimeKind.Utc) : null,
            ExecutionBefore = Before is { } before ? DateTime.SpecifyKind(before, DateTimeKind.Utc) : null,
            ActionableFailuresOnly = ActionableOnly,
            Text = string.IsNullOrEmpty(Search) ? null : Search,
        };
}

/// <summary>
/// The checks the module's requests share. Their rules are <see cref="DashboardValidation"/>'s, the words the shell's
/// command validation uses, so a form reads a refused field the same way from either.
/// </summary>
internal static class JobsRequestRules
{
    public static bool IsEnumOrEmpty<TEnum>(string? value)
        where TEnum : struct, Enum =>
        string.IsNullOrEmpty(value) || DashboardValidation.ParseEnum<TEnum>(value) is not null;

    public static IRuleBuilderOptions<T, int> JobsRetries<T>(this IRuleBuilder<T, int> rule) =>
        rule.GreaterThanOrEqualTo(0)
            .WithErrorCode(DashboardValidation.MinimumRule)
            .WithMessage("Retries cannot be negative.")
            .LessThanOrEqualTo(JobsCommands.MaxRetries)
            .WithErrorCode(DashboardValidation.MaximumRule)
            .WithMessage($"Retries cannot be more than {JobsCommands.MaxRetries}.");

    public static IRuleBuilderOptions<T, int> JobsRetryInterval<T>(this IRuleBuilder<T, int> rule) =>
        rule.GreaterThanOrEqualTo(0)
            .WithErrorCode(DashboardValidation.MinimumRule)
            .WithMessage("Each retry interval is a number of seconds, zero or more.");

    public static IRuleBuilderOptions<T, JsonElement?> JobsRequest<T>(this IRuleBuilder<T, JsonElement?> rule) =>
        rule.Must(static request =>
                request is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value
                || value.GetRawText().Length <= JobsCommands.MaxRequestLength
            )
            .WithErrorCode(DashboardValidation.MaxLengthRule)
            .WithMessage($"The request must be at most {JobsCommands.MaxRequestLength} characters of JSON.");
}
