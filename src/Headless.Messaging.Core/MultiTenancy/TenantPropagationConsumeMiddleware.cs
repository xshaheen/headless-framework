// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.MultiTenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.MultiTenancy;

/// <summary>
/// Restores <see cref="ICurrentTenant"/> from the resolved consume tenant for the inner handler, and attaches the
/// tenant to the handler's logs and the current span as configured by <see cref="TenantTelemetryOptions"/>.
/// </summary>
[PublicAPI]
public sealed class TenantPropagationConsumeMiddleware(
    ICurrentTenant currentTenant,
    ILogger<TenantPropagationConsumeMiddleware>? logger = null,
    IOptions<TenantTelemetryOptions>? telemetryOptions = null
) : IConsumeMiddleware<ConsumeContext>
{
    /// <summary>Framework priority for tenant restoration middleware.</summary>
    public const int Priority = -1000;

    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);
    private readonly TenantTelemetryOptions? _telemetryOptions = telemetryOptions?.Value;

    /// <inheritdoc/>
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(next);

        if (context.TenantId is not { } value)
        {
            await next().ConfigureAwait(false);
            return;
        }

        logger?.TenantContextSwitched(value);

        var alreadyAmbient = string.Equals(_currentTenant.Id, value, StringComparison.Ordinal);
        using var scope = _currentTenant.Change(value);
        using var telemetryScope = TenantContextScope.EnrichTelemetry(logger, _telemetryOptions, value, alreadyAmbient);
        await next().ConfigureAwait(false);
    }
}
