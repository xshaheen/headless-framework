// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;

namespace Headless.Idempotency;

/// <summary>Settings of the Idempotency dashboard module: the view and operate policies every module carries.</summary>
/// <remarks>
/// The module is read-only, so only <see cref="DashboardModuleOptions.ViewPolicy"/> gates it: a principal that passes
/// it reads any tenant's record metadata.
/// </remarks>
[PublicAPI]
public sealed class IdempotencyDashboardOptions : DashboardModuleOptions
{
    /// <summary>The id the Idempotency module serves under: its URL segment and API group.</summary>
    public const string ModuleId = "idempotency";
}

internal sealed class IdempotencyDashboardOptionsValidator : AbstractValidator<IdempotencyDashboardOptions>
{
    public IdempotencyDashboardOptionsValidator()
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
