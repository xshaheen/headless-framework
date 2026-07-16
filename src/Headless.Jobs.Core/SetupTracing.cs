// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace OpenTelemetry.Trace;

/// <summary>
/// OpenTelemetry tracing registration for the Jobs subsystem. Lives in the
/// <c>OpenTelemetry.Trace</c> namespace so it surfaces next to the provider builder without an
/// extra <c>using Headless.Jobs</c> import; only <c>OpenTelemetry.Api</c> surface is used (no SDK dependency).
/// </summary>
[PublicAPI]
public static class HeadlessJobsTracingExtensions
{
    /// <summary>
    /// Enables Jobs tracing by subscribing to the <see cref="JobsDiagnostics.SourceName"/> activity
    /// source. This is the single opt-in: the default <c>IJobsInstrumentation</c> already emits
    /// <see cref="System.Diagnostics.Activity"/> spans, gated on listener presence, so no additional
    /// registration step is needed.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> being configured.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TracerProviderBuilder AddJobsInstrumentation(this TracerProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddSource(JobsDiagnostics.SourceName);
    }
}
