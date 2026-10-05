// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>Tags carried by the health checks that Headless provider packages contribute.</summary>
/// <remarks>
/// Every contributed check carries <see cref="Ready" /> and <see cref="Headless" />, plus one tag naming the kind of
/// dependency it probes. None carries the liveness tag, so a liveness endpoint that filters on it never restarts a
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
}
