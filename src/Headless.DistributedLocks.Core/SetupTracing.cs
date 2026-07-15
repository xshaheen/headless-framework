// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DistributedLocks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace OpenTelemetry.Trace;

/// <summary>
/// OpenTelemetry tracing registration for the distributed-locks subsystem. Lives in the
/// <c>OpenTelemetry.Trace</c> namespace so it surfaces next to the provider builder without an
/// extra <c>using</c>; only <c>OpenTelemetry.Api</c> surface is used (no SDK dependency).
/// </summary>
[PublicAPI]
public static class HeadlessDistributedLocksTracingExtensions
{
    /// <summary>
    /// Enables distributed-locks tracing by subscribing to the
    /// <see cref="DistributedLocksDiagnostics.SourceName"/> activity source.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> being configured.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TracerProviderBuilder AddDistributedLocksInstrumentation(this TracerProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddSource(DistributedLocksDiagnostics.SourceName);
    }
}
