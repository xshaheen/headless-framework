// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.Core;
using Headless.Messaging.Messages;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal static class TenantContextScope
{
    private static readonly TenantTelemetryOptions _DefaultTelemetryOptions = new();

    public static string? ResolveTenantId(IDictionary<string, string?> headers, ILogger? logger)
    {
        if (!headers.TryGetValue(Headers.TenantId, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > MessageOptions.TenantIdMaxLength)
        {
            logger?.TenantIdHeaderRejected(value.Length);
            return null;
        }

        return value;
    }

    public static IDisposable? ChangeFromEnvelope(IServiceProvider serviceProvider, Message message, ILogger? logger)
    {
        var tenantId = ResolveTenantId(message.Headers, logger);
        if (tenantId is null)
        {
            return null;
        }

        var currentTenant = serviceProvider.GetService<ICurrentTenant>();
        if (currentTenant is null)
        {
            return null;
        }

        logger?.TenantContextSwitched(tenantId);

        var alreadyAmbient = string.Equals(currentTenant.Id, tenantId, StringComparison.Ordinal);
        var tenantScope = currentTenant.Change(tenantId);
        var telemetryScope = EnrichTelemetry(
            logger,
            serviceProvider.GetService<IOptions<TenantTelemetryOptions>>()?.Value,
            tenantId,
            alreadyAmbient
        );

        return telemetryScope is null
            ? tenantScope
            : DisposableFactory.Create(
                (Tenant: tenantScope, Telemetry: telemetryScope),
                static scopes =>
                {
                    scopes.Telemetry.Dispose();
                    scopes.Tenant.Dispose();
                }
            );
    }

    /// <summary>Attaches the tenant to the current span and, unless it was already ambient, to a logging scope.</summary>
    /// <remarks>
    /// The transactional inbox path switches the tenant from the envelope before the consume middleware runs, so the
    /// middleware finds its tenant already ambient. It still tags the span current at that point, which may be a
    /// consumer span started since, but skips a second logging scope that would repeat the property on every record.
    /// </remarks>
    public static IDisposable? EnrichTelemetry(
        ILogger? logger,
        TenantTelemetryOptions? options,
        string tenantId,
        bool alreadyAmbient
    )
    {
        options ??= _DefaultTelemetryOptions;

        if (alreadyAmbient)
        {
            TenantTelemetry.TagActivity(Activity.Current, options, tenantId);
            return null;
        }

        return TenantTelemetry.Enrich(logger, options, tenantId);
    }
}
