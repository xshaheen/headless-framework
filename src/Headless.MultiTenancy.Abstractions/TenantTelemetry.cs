// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;

namespace Headless.MultiTenancy;

/// <summary>Attaches the tenant id to a span as configured by <see cref="TenantTelemetryOptions"/>.</summary>
/// <remarks>
/// Spans that start while a tenant is ambient are tagged by the telemetry pipeline. This is for spans that start
/// before their tenant is known: the ASP.NET Core request span, or a messaging or job span tagged from its envelope.
/// </remarks>
[PublicAPI]
public static class TenantTelemetry
{
    /// <summary>Tags <paramref name="activity"/> with the tenant id when trace enrichment is enabled.</summary>
    /// <param name="activity">The span to tag; <see langword="null"/> or an unrecorded span is ignored.</param>
    /// <param name="options">The telemetry options.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="tenantId"/> is <see langword="null"/>.</exception>
    public static void TagActivity(Activity? activity, TenantTelemetryOptions options, string tenantId)
    {
        Argument.IsNotNull(options);
        Argument.IsNotNull(tenantId);

        if (options.EnrichTraces && activity is { IsAllDataRequested: true })
        {
            activity.SetTag(options.AttributeName, tenantId);
        }
    }
}
