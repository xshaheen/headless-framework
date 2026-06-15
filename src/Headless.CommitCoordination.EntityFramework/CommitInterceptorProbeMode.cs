// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Controls how the startup commit-interceptor self-probe reacts when the EF commit-coordination interceptor is
/// registered but is not actually firing for a <c>DbContext</c> (the silent "transactional outbox enabled but
/// mis-wired" footgun). Mirrors the SQL Server diagnostic self-probe posture.
/// </summary>
[PublicAPI]
public enum CommitInterceptorProbeMode
{
    /// <summary>
    /// Skip the probe entirely. The escape-hatch when the per-host-start round-trip (an empty-transaction commit
    /// against the database) is unwanted — e.g. a cold-start latency budget or an environment where the database is
    /// not reachable at boot. The trade-off is losing early mis-wire detection: an interceptor that is registered but
    /// not firing stays silent until a real publish drains as a rollback. The durable outbox row + relay sweep still
    /// recover the work regardless; only the early signal is lost.
    /// </summary>
    Disabled = 0,

    /// <summary>Log a loud warning when the interceptor is not firing, but let the host start. The default.</summary>
    Warn = 1,

    /// <summary>Throw at startup when the interceptor is not firing, failing the host start.</summary>
    Strict = 2,
}
