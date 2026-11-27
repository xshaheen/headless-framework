// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;

namespace Headless.Sequences;

/// <summary>
/// Settings of the Sequences dashboard module: the view and operate policies every module carries. The module is
/// read-only, so only <see cref="DashboardModuleOptions.ViewPolicy"/> guards a route.
/// </summary>
[PublicAPI]
public sealed class SequencesDashboardOptions : DashboardModuleOptions
{
    /// <summary>The id the Sequences module serves under: its URL segment and API group.</summary>
    public const string ModuleId = "sequences";
}

internal sealed class SequencesDashboardOptionsValidator : AbstractValidator<SequencesDashboardOptions>
{
    public SequencesDashboardOptionsValidator()
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
