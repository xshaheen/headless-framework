// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;

namespace Headless.RateLimiting;

/// <summary>Settings of the Rate limits dashboard module: the view and operate policies every module carries.</summary>
[PublicAPI]
public sealed class RateLimitingDashboardOptions : DashboardModuleOptions
{
    /// <summary>The id the Rate limits module serves under: its URL segment, API group, and command prefix.</summary>
    public const string ModuleId = "rate-limiting";
}

internal sealed class RateLimitingDashboardOptionsValidator : AbstractValidator<RateLimitingDashboardOptions>
{
    public RateLimitingDashboardOptionsValidator()
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
