// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Jobs.MultiTenancy;

/// <summary>
/// Restores the job's persisted tenant via <see cref="ICurrentTenant.Change"/> around every handler attempt when
/// <see cref="JobsTenancyOptions.PropagateTenant"/> is enabled. The scope is opened inside this frame — the frame that
/// awaits the handler — so the AsyncLocal tenant flows down into the handler and is always reverted on dispose,
/// whether the attempt succeeds, faults, or cancels. Polly re-dispatches this pipeline per attempt, so each retry is
/// freshly scoped. A <see langword="null"/> tenant runs the attempt system scope.
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

        if (!_options.PropagateTenant)
        {
            await next(cancellationToken).ConfigureAwait(false);

            return;
        }

        var tenantId = context.Execution.TenantId;

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
