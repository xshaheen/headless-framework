// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;

namespace Headless.Cluster;

/// <summary>
/// Settings of the Cluster dashboard module: the view and operate policies every dashboard module carries, and how
/// many node changes one live read returns.
/// </summary>
[PublicAPI]
public sealed class ClusterDashboardOptions : DashboardModuleOptions
{
    /// <summary>The id the Cluster module serves under: its URL segment, API group, and stream topic prefix.</summary>
    public const string ModuleId = "cluster";

    /// <summary>
    /// Gets or sets the most node changes one live read returns. Defaults to 200; a larger burst, such as a whole
    /// cluster restarting, is read over several polls.
    /// </summary>
    public int ChangePageSize { get; set; } = 200;
}

internal sealed class ClusterDashboardOptionsValidator : AbstractValidator<ClusterDashboardOptions>
{
    public ClusterDashboardOptionsValidator()
    {
        // A white-space policy name passes the shell's "empty means inherit" check but names no host policy, so
        // authorization would fail on every request instead of at startup.
        RuleFor(x => x.ViewPolicy)
            .Must(static policy => string.IsNullOrEmpty(policy) || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("ViewPolicy must name a host authorization policy, or be empty to use the shell's.");

        RuleFor(x => x.OperatePolicy)
            .Must(static policy => string.IsNullOrEmpty(policy) || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("OperatePolicy must name a host authorization policy, or be empty to use the view policy.");

        RuleFor(x => x.ChangePageSize)
            .InclusiveBetween(1, 1000)
            .WithMessage("ChangePageSize must be between 1 and 1000.");
    }
}
