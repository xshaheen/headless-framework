// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Hosting;

/// <summary>
/// What an application can change about one health check a Headless provider package contributes. Configure it under
/// the check's name with <c>services.ConfigureHeadlessHealthCheck(name, options =&gt; ...)</c>; a check nobody configures
/// keeps the defaults.
/// </summary>
[PublicAPI]
public sealed class HeadlessHealthCheckOptions
{
    /// <summary>
    /// The status a failed or timed-out probe reports. <see cref="HealthStatus.Degraded" /> keeps a non-critical
    /// dependency from failing readiness. Default: <see cref="HealthStatus.Unhealthy" />.
    /// </summary>
    public HealthStatus FailureStatus { get; set; } = HealthStatus.Unhealthy;

    /// <summary>
    /// How long the probe may run before it reports <see cref="FailureStatus" /> with a timeout description. Default:
    /// <see langword="null" />, no limit beyond the driver's own timeouts and the health request's cancellation.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Tags added to the check's own, for example to route it to a separate readiness endpoint.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// The command the probe runs, where the check supports one; checks that do not ignore it. The SQL and DbContext
    /// checks run it as their query (default <c>SELECT 1</c> / <c>Database.CanConnectAsync</c>), for example a query
    /// that also proves a schema object exists. Default: <see langword="null" />, the check's own probe.
    /// </summary>
    public string? TestCommand { get; set; }
}

internal sealed class HeadlessHealthCheckOptionsValidator : AbstractValidator<HeadlessHealthCheckOptions>
{
    public HeadlessHealthCheckOptionsValidator()
    {
        RuleFor(x => x.FailureStatus).IsInEnum().NotEqual(HealthStatus.Healthy);
        RuleFor(x => x.Timeout).GreaterThan(TimeSpan.Zero).When(x => x.Timeout.HasValue);
        RuleFor(x => x.Tags).NotNull();
        RuleForEach(x => x.Tags).NotEmpty();
        RuleFor(x => x.TestCommand).NotEmpty().When(x => x.TestCommand is not null);
    }
}
