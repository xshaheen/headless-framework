// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>Tags carried by the health checks that Headless provider packages contribute.</summary>
/// <remarks>
/// Every contributed check carries <see cref="Ready" /> and <see cref="Headless" />, plus one tag naming the kind of
/// dependency it probes. A check that reads the running host's own state, rather than probing a dependency, also
/// carries <see cref="HostState" />. None carries the liveness tag, so a liveness endpoint that filters on it never restarts a
/// process because a database or broker is down.
/// </remarks>
[PublicAPI]
public static class HeadlessHealthCheckTags
{
    /// <summary>Marks a check that decides whether the process can serve traffic (readiness).</summary>
    public const string Ready = "ready";

    /// <summary>Marks a check contributed by a Headless package rather than by the application.</summary>
    public const string Headless = "headless";

    /// <summary>Marks a check that probes a relational database.</summary>
    public const string Database = "database";

    /// <summary>Marks a check that probes a Redis server.</summary>
    public const string Redis = "redis";

    /// <summary>Marks a check that probes a message broker.</summary>
    public const string Messaging = "messaging";

    /// <summary>Marks a check that probes a blob store.</summary>
    public const string Blobs = "blobs";

    /// <summary>Marks a check that reports whether a Jobs host can run jobs: its activation, scheduler, or job store.</summary>
    public const string Jobs = "jobs";

    /// <summary>
    /// Marks a check that reports the state of the running host, such as whether a background service has started,
    /// rather than probing a dependency.
    /// </summary>
    /// <remarks>
    /// A host that was never started always fails such a check, so the <c>check</c> command of
    /// <c>RunHeadlessAsync</c> skips it. Tag a check of your own with it when it, too, reads state that only a
    /// started host has.
    /// </remarks>
    public const string HostState = "host-state";
}
