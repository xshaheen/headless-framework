// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;

namespace Headless.Jobs;

/// <summary>
/// Settings of the Jobs dashboard module: the view and operate policies every dashboard module carries, and how the
/// module reads changes from the Jobs store.
/// </summary>
[PublicAPI]
public sealed class JobsDashboardOptions : DashboardModuleOptions
{
    /// <summary>The id the Jobs module serves under: its URL segment, API group, and stream topic prefix.</summary>
    public const string ModuleId = "jobs";

    /// <summary>
    /// Gets or sets how far back each change read reads again, so a write that committed after a later one is still
    /// seen. Defaults to 5 seconds; make it longer than the commit delay of the Jobs store's slowest single write.
    /// </summary>
    public TimeSpan ChangeOverlapWindow { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the most changed rows one change read returns. Defaults to 200; a larger burst is read over several
    /// polls.
    /// </summary>
    public int ChangePageSize { get; set; } = 200;
}

internal sealed class JobsDashboardOptionsValidator : AbstractValidator<JobsDashboardOptions>
{
    public JobsDashboardOptionsValidator()
    {
        // A white-space policy name passes the shell's "empty means inherit" check but names no host policy, so
        // authorization would fail on every request instead of at startup.
        RuleFor(x => x.ViewPolicy)
            .Must(static policy => string.IsNullOrEmpty(policy) || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("ViewPolicy must name a host authorization policy, or be empty to use the shell's.");

        RuleFor(x => x.OperatePolicy)
            .Must(static policy => string.IsNullOrEmpty(policy) || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("OperatePolicy must name a host authorization policy, or be empty to use the view policy.");

        RuleFor(x => x.ChangeOverlapWindow)
            .GreaterThanOrEqualTo(TimeSpan.Zero)
            .WithMessage("ChangeOverlapWindow must not be negative.");

        RuleFor(x => x.ChangePageSize)
            .InclusiveBetween(1, 1000)
            .WithMessage("ChangePageSize must be between 1 and 1000.");
    }
}
