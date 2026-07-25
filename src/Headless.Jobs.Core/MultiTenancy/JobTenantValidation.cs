// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Exceptions;
using Headless.Jobs.Models;

namespace Headless.Jobs.MultiTenancy;

/// <summary>
/// Shared schedule-time tenant validation rules. Applied by both the root resolution
/// (<see cref="TenantPropagationScheduleMiddleware"/>) and the chain-descendant walk in <c>JobsManager</c> — the two
/// call sites cannot share a resolver because the middleware sees only <c>BaseJobEntity</c> while the descendant walk
/// needs the generic <c>Children</c> collection, but the rules and messages must not drift apart.
/// </summary>
internal static class JobTenantValidation
{
    /// <summary>Shared rejection message for any path that finds a tenant on a cron definition (R8).</summary>
    internal const string CronSystemScopeMessage =
        "Cron definitions are always system scope and cannot carry a tenant identifier.";

    /// <summary>Shared rejection message for the opt-in lateral cross-tenant guard.</summary>
    internal const string CrossTenantEnqueueMessage =
        "The explicit tenant identifier differs from the present ambient tenant; cross-tenant enqueue is rejected "
        + "because RejectCrossTenantEnqueue is enabled.";

    /// <summary>
    /// Returns whether <paramref name="explicitTenantId"/> targets a different tenant than the present ambient
    /// tenant; throws when <paramref name="rejectCrossTenant"/> is enabled. A blank ambient is not a real tenant
    /// and never counts as a mismatch, so explicit values from system scope are always honored.
    /// </summary>
    internal static bool CheckCrossTenant(string explicitTenantId, string? ambientTenantId, bool rejectCrossTenant)
    {
        var crossTenant =
            !string.IsNullOrWhiteSpace(ambientTenantId)
            && !string.Equals(ambientTenantId, explicitTenantId, StringComparison.Ordinal);

        if (crossTenant && rejectCrossTenant)
        {
            throw new JobValidatorException(CrossTenantEnqueueMessage);
        }

        return crossTenant;
    }

    /// <summary>Rejects a blank or over-length explicit tenant identifier.</summary>
    internal static void ValidateExplicitTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new JobValidatorException("A tenant identifier must not be blank.");
        }

        if (tenantId.Length > JobsTenancyOptions.TenantIdMaxLength)
        {
            throw new JobValidatorException(
                $"The tenant identifier length ({tenantId.Length}) exceeds the maximum of {JobsTenancyOptions.TenantIdMaxLength}."
            );
        }
    }

    /// <summary>Rejects the system-job contradiction and escalation cases (R7).</summary>
    internal static void ValidateSystemJob(string? explicitTenantId, bool ambientPresent)
    {
        if (explicitTenantId is not null)
        {
            throw new JobValidatorException("A system job cannot also specify an explicit tenant identifier.");
        }

        if (ambientPresent)
        {
            throw new JobValidatorException(
                "A system job cannot be scheduled while an ambient tenant is present; tenant code cannot escalate to system scope."
            );
        }
    }
}
