// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Controls how the ambient tenant id is attached to logs and spans. Every channel is on by default; a host needs no
/// registration to get the defaults.
/// </summary>
/// <remarks>
/// Log records and spans that start while a tenant is ambient are enriched by the ServiceDefaults OpenTelemetry
/// pipeline, which reads <see cref="ICurrentTenant"/> at that moment. The request, consume, and job spans start before
/// their tenant is ambient, so the framework tags those directly. A host outside ServiceDefaults, or one logging
/// through a provider other than OpenTelemetry, gets only those direct span tags.
/// </remarks>
[PublicAPI]
public sealed class TenantTelemetryOptions
{
    /// <summary>
    /// The default log attribute name: <c>TenantId</c>, matching the <c>{TenantId}</c> placeholder the framework
    /// already uses in its log message templates, so one filter finds both.
    /// </summary>
    public const string DefaultLogAttributeName = "TenantId";

    /// <summary>
    /// The default span and metric attribute name: <c>tenant.id</c>. OpenTelemetry semantic conventions (v1.44.0)
    /// register no general tenant attribute; <c>tenant.id</c> follows their <c>namespace.attribute</c> shape and the
    /// <c>tenant.*</c> namespace proposed upstream, so a future registered attribute is likely to match it.
    /// </summary>
    public const string DefaultAttributeName = "tenant.id";

    /// <summary>
    /// Whether every log record written while a tenant is ambient carries the tenant id. Default:
    /// <see langword="true"/>.
    /// </summary>
    public bool EnrichLogs { get; set; } = true;

    /// <summary>The log record attribute that carries the tenant id. Default: <see cref="DefaultLogAttributeName"/>.</summary>
    public string LogAttributeName { get; set; } = DefaultLogAttributeName;

    /// <summary>
    /// Whether spans carry the tenant id: the request, consume, and job spans, and every span started while a tenant is
    /// ambient. Default: <see langword="true"/>.
    /// </summary>
    public bool EnrichTraces { get; set; } = true;

    /// <summary>
    /// The span attribute that carries the tenant id, also used by framework meters that opt into a tenant dimension.
    /// Default: <see cref="DefaultAttributeName"/>.
    /// </summary>
    public string AttributeName { get; set; } = DefaultAttributeName;
}
