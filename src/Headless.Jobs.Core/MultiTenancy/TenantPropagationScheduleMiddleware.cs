// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Jobs.MultiTenancy;

/// <summary>
/// Resolves and validates the tenant on the root scheduling entity before validation and persistence: an explicit
/// <see cref="BaseJobEntity.TenantId"/> wins, otherwise the ambient <see cref="ICurrentTenant.Id"/> is captured when
/// <see cref="JobsTenancyOptions.PropagateTenant"/> is enabled. Structural validation (cron scope, system-job
/// contradictions, blank/over-length values) always runs regardless of options; ambient capture and strict
/// enforcement are the only options-gated behaviors. Chain descendants are resolved by the generic
/// <c>JobsManager</c> tree walk, which can reach the typed <c>Children</c> that this
/// <see cref="BaseJobEntity"/>-typed context cannot.
/// </summary>
[PublicAPI]
public sealed class TenantPropagationScheduleMiddleware(
    ICurrentTenant currentTenant,
    IOptions<JobsTenancyOptions> options,
    ILogger<TenantPropagationScheduleMiddleware>? logger = null
) : IJobScheduleMiddleware
{
    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);
    private readonly JobsTenancyOptions _options = Argument.IsNotNull(options).Value;

    /// <inheritdoc/>
    public async Task InvokeAsync(
        JobScheduleContext context,
        JobScheduleNext next,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(next);

        _ResolveRootTenant(context.Job);

        await next(cancellationToken).ConfigureAwait(false);
    }

    private void _ResolveRootTenant(BaseJobEntity job)
    {
        // Cron is always system scope: a tenant-scoped cron definition is a contract violation, and ambient capture
        // never applies to it (IsSystemJob is inert on cron).
        if (job is CronJobEntity)
        {
            if (job.TenantId is not null)
            {
                throw new JobValidatorException(
                    "Cron definitions are always system scope and cannot carry a tenant identifier."
                );
            }

            return;
        }

        // System-job bypass: a deliberate tenantless job. Reject the escalation and contradiction cases, otherwise keep
        // TenantId null and record the decision (R7).
        if (job.IsSystemJob)
        {
            // A blank ambient is not a real tenant, so it does not escalate a system job into a tenant scope.
            JobTenantValidation.ValidateSystemJob(job.TenantId, !string.IsNullOrWhiteSpace(_currentTenant.Id));

            logger?.SystemJobScheduled(job.Function);

            return;
        }

        // Explicit tenant wins over ambient, even when it differs (documented in-process trust model). Validate it.
        if (job.TenantId is { } explicitTenant)
        {
            JobTenantValidation.ValidateExplicitTenantId(explicitTenant);

            return;
        }

        // No explicit tenant: capture the ambient tenant when propagation is enabled.
        if (_options.PropagateTenant && _currentTenant.Id is { } ambientTenant)
        {
            // Fail closed: a present-but-unusable ambient tenant (blank or over-length) rejects the enqueue rather than
            // silently downgrading a tenant job to system scope. Log only the length, never the value.
            if (string.IsNullOrWhiteSpace(ambientTenant) || ambientTenant.Length > JobsTenancyOptions.TenantIdMaxLength)
            {
                logger?.AmbientTenantRejected(ambientTenant.Length);

                throw new JobValidatorException(
                    $"The ambient tenant identifier was rejected: its length ({ambientTenant.Length}) is blank or exceeds the maximum of {JobsTenancyOptions.TenantIdMaxLength}."
                );
            }

            job.TenantId = ambientTenant;

            return;
        }

        // Still tenantless and not a system job: strict mode rejects.
        if (_options.TenantContextRequired)
        {
            throw new MissingTenantContextException();
        }
    }
}

internal static partial class TenantPropagationScheduleMiddlewareLog
{
    [LoggerMessage(
        EventId = 3220,
        EventName = "JobSystemScopeScheduled",
        Level = LogLevel.Debug,
        Message = "Scheduling a system-scope (tenantless) job for function '{Function}'."
    )]
    public static partial void SystemJobScheduled(this ILogger logger, string function);

    [LoggerMessage(
        EventId = 3221,
        EventName = "JobAmbientTenantRejected",
        Level = LogLevel.Warning,
        Message = "The ambient tenant identifier was rejected by Jobs tenant propagation because its length ({Length}) is blank or exceeds JobsTenancyOptions.TenantIdMaxLength. The enqueue fails closed; investigate the ambient tenant source if this repeats."
    )]
    public static partial void AmbientTenantRejected(this ILogger logger, int length);
}
