// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>Names of the health checks <c>Headless.Hosting</c> itself registers.</summary>
[PublicAPI]
public static class HeadlessHealthCheckNames
{
    /// <summary>
    /// The readiness check that reports whether every warm-up that gates readiness has finished. It names each pending
    /// warm-up and its last error, and carries <see cref="HeadlessHealthCheckTags.HostState" />.
    /// </summary>
    public const string Warmup = "warmup";
}
