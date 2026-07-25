// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Jobs.MultiTenancy;

/// <summary>
/// Restores the job's persisted tenant via <see cref="ICurrentTenant.Change"/> around every handler attempt whenever
/// the job carries a <see cref="JobExecutionState.TenantId"/>. The schedule side persists an explicit/captured tenant
/// regardless of <see cref="JobsTenancyOptions.PropagateTenant"/>, so an explicitly-tenanted job is always restored —
/// even on a host with propagation off. The scope is opened inside this frame — the frame that awaits the handler —
/// so the AsyncLocal tenant flows down into the handler and is always reverted on dispose, whether the attempt
/// succeeds, faults, or cancels. Polly re-dispatches this pipeline per attempt, so each retry is freshly scoped. When
/// <see cref="JobsTenancyOptions.PropagateTenant"/> is enabled a <see langword="null"/> tenant still clears a leaked
/// ambient so the attempt runs system scope; a genuinely tenant-free host (no persisted tenant, propagation off) is a
/// pure pass-through.
/// </summary>
[PublicAPI]
public sealed class TenantRestoreExecuteMiddleware(
    ICurrentTenant currentTenant,
    IOptions<JobsTenancyOptions> options,
    ILogger<TenantRestoreExecuteMiddleware>? logger = null
) : IJobExecuteMiddleware
{
    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);
    private readonly JobsTenancyOptions _options = Argument.IsNotNull(options).Value;

    /// <inheritdoc/>
    public async Task InvokeAsync(
        JobExecuteContext context,
        JobExecuteNext next,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(next);

        var tenantId = context.Execution.TenantId;

        // Restore whenever the row carries a persisted tenant, even with propagation off: the schedule side writes an
        // explicit/captured tenant regardless of the flag, so an explicitly-tenanted job must always run under its
        // tenant. Only a genuinely tenant-free host (no persisted tenant and propagation off) short-circuits — a
        // propagation-enabled host still opens Change(null) below to clear a leaked ambient for system jobs.
        if (tenantId is null && !_options.PropagateTenant)
        {
            await next(cancellationToken).ConfigureAwait(false);

            return;
        }

        // Change is opened in THIS frame so the AsyncLocal set flows down into the awaited handler; a null tenant clears
        // the scope so the attempt runs system scope even if an ambient tenant leaked onto the worker. The scope reverts
        // to the prior ambient on dispose — success, fault, or cancellation.
        using var scope = _currentTenant.Change(tenantId);

        if (tenantId is not null)
        {
            logger?.TenantScopeRestored(tenantId);
        }

        await next(cancellationToken).ConfigureAwait(false);
    }
}

internal static partial class TenantRestoreExecuteMiddlewareLog
{
    [LoggerMessage(
        EventId = 3222,
        EventName = "JobTenantScopeRestored",
        Level = LogLevel.Debug,
        Message = "Restoring ICurrentTenant to tenant '{TenantId}' for the job execution attempt."
    )]
    public static partial void TenantScopeRestored(this ILogger logger, string tenantId);
}
