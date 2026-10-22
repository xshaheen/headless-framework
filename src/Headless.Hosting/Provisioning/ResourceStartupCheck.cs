// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>What a starting host does when it finds a provisioned resource missing or changed.</summary>
[PublicAPI]
public enum ResourceStartupCheck
{
    /// <summary>The host does not check the resource when it starts; only <c>resources verify</c> does.</summary>
    None = 0,

    /// <summary>
    /// The host checks the resource once after it starts. Until the check passes, the <c>warmup</c> readiness check
    /// reports <c>Unhealthy</c> and names the resource, and the check is retried with back-off. The host keeps running,
    /// so an orchestrator sends it no traffic instead of restarting it.
    /// </summary>
    GateReadiness = 1,

    /// <summary>
    /// The host checks the resource before any hosted service starts and fails the start when it is missing or
    /// changed. For a resource the host's own background services touch as soon as they start, such as tables.
    /// </summary>
    FailStart = 2,
}
