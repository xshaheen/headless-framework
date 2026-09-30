// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.MultiTenancy;

/// <summary>
/// Controls how the tenancy entry points (HTTP resolution, message consume and exhausted callbacks, job execution)
/// attach the resolved tenant id to logs, traces, and metrics. Every channel is on by default; a host needs no
/// registration to get the defaults.
/// </summary>
[PublicAPI]
public sealed class TenantTelemetryOptions
{
    /// <summary>
    /// The default log scope property name: <c>TenantId</c>, matching the <c>{TenantId}</c> placeholder the framework
    /// already uses in its log message templates, so one filter finds both.
    /// </summary>
    public const string DefaultLogScopePropertyName = "TenantId";

    /// <summary>
    /// The default span and metric attribute name: <c>tenant.id</c>. OpenTelemetry semantic conventions (v1.44.0)
    /// register no general tenant attribute; <c>tenant.id</c> follows their <c>namespace.attribute</c> shape and the
    /// <c>tenant.*</c> namespace proposed upstream, so a future registered attribute is likely to match it.
    /// </summary>
    public const string DefaultAttributeName = "tenant.id";

    /// <summary>
    /// Whether the entry points open a structured logging scope carrying the tenant id for the duration of the work.
    /// Default: <see langword="true"/>.
    /// </summary>
    public bool EnrichLogs { get; set; } = true;

    /// <summary>The logging scope property that carries the tenant id. Default: <see cref="DefaultLogScopePropertyName"/>.</summary>
    public string LogScopePropertyName { get; set; } = DefaultLogScopePropertyName;

    /// <summary>
    /// Whether the entry points tag <see cref="System.Diagnostics.Activity.Current"/> with the tenant id. Only the span
    /// that is current at the entry point is tagged; child spans are correlated to it by trace id. Default:
    /// <see langword="true"/>.
    /// </summary>
    public bool EnrichTraces { get; set; } = true;

    /// <summary>
    /// Whether <see cref="TenantTelemetry.TryGetMetricTag"/> returns a tag for the ambient tenant. Default:
    /// <see langword="true"/>. The framework adds no tenant tag to its own meters through this switch; application
    /// meters opt in per measurement, and each distinct tenant id becomes a separate time series.
    /// </summary>
    public bool EnrichMetrics { get; set; } = true;

    /// <summary>The span and metric attribute that carries the tenant id. Default: <see cref="DefaultAttributeName"/>.</summary>
    public string AttributeName { get; set; } = DefaultAttributeName;
}

internal sealed class TenantTelemetryOptionsValidator : AbstractValidator<TenantTelemetryOptions>
{
    public TenantTelemetryOptionsValidator()
    {
        RuleFor(x => x.LogScopePropertyName).NotEmpty().When(x => x.EnrichLogs);
        RuleFor(x => x.AttributeName).NotEmpty().When(x => x.EnrichTraces || x.EnrichMetrics);
    }
}
