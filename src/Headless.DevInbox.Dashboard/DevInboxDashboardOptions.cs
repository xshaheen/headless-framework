// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;

namespace Headless.DevInbox.Dashboard;

/// <summary>
/// Settings of the Dev inbox dashboard module: the view and operate policies every dashboard module carries.
/// </summary>
[PublicAPI]
public sealed class DevInboxDashboardOptions : DashboardModuleOptions
{
    /// <summary>The id the Dev inbox module serves under: its URL segment, API group, and stream topic prefix.</summary>
    public const string ModuleId = "dev-inbox";
}

internal sealed class DevInboxDashboardOptionsValidator : AbstractValidator<DevInboxDashboardOptions>
{
    public DevInboxDashboardOptionsValidator()
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
