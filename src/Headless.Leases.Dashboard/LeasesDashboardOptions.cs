// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;

namespace Headless.Leases;

/// <summary>
/// Settings of the Leases dashboard module: the view and operate policies every dashboard module carries. The module
/// is read-only, so the operate policy guards nothing it serves today.
/// </summary>
[PublicAPI]
public sealed class LeasesDashboardOptions : DashboardModuleOptions
{
    /// <summary>The id the Leases module serves under: its URL segment, API group, and list topic prefix.</summary>
    public const string ModuleId = "leases";
}

internal sealed class LeasesDashboardOptionsValidator : AbstractValidator<LeasesDashboardOptions>
{
    public LeasesDashboardOptionsValidator()
    {
        // A white-space policy name passes the shell's "empty means inherit" check but names no host policy, so
        // authorization would fail on every request instead of at startup.
        RuleFor(x => x.ViewPolicy)
            .Must(static policy => string.IsNullOrEmpty(policy) || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("ViewPolicy must name a host authorization policy, or be empty to use the shell's.");

        RuleFor(x => x.OperatePolicy)
            .Must(static policy => string.IsNullOrEmpty(policy) || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("OperatePolicy must name a host authorization policy, or be empty to use the view policy.");
    }
}
