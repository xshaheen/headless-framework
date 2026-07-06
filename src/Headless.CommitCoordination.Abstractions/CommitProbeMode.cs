// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Controls how a commit-coordination startup self-probe reacts when it cannot verify that out-of-band commit
/// detection is wired end-to-end — the silent "commit coordination enabled but mis-wired" footgun. Shared by every
/// commit-coordination provider (the EF interceptor self-probe and the SQL Server diagnostic self-probe): each
/// verifies a different signal path, but the reaction posture — skip, warn, or fail startup — is identical, so the
/// mode is a single shared contract rather than a per-provider enum.
/// </summary>
/// <remarks>
/// The probe is only an <b>early</b> signal: a mis-wired interceptor or diagnostic observer that never fires is
/// recovered regardless by the durable outbox row plus the relay sweep — commit coordination is a dispatch
/// accelerator, not the durability mechanism. That is why <see cref="Warn" /> is the default: surfacing the mis-wire
/// loudly without blocking startup trades early detection for boot resilience, and no work is lost either way.
/// </remarks>
[PublicAPI]
public enum CommitProbeMode
{
    /// <summary>
    /// Skip the self-probe entirely. The escape hatch when the per-host-start round-trip against the database is
    /// unwanted — e.g. a cold-start latency budget or an environment where the database is not reachable at boot.
    /// The trade-off is losing early mis-wire detection: an interceptor or diagnostic observer that is registered
    /// but not firing stays silent until a real commit relies on it, at which point the durable outbox row and
    /// relay sweep recover the work regardless; only the early signal is lost.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Run the probe; log a loud warning (and record the provider's degraded/skipped status where applicable) when
    /// compatibility cannot be verified, but allow the host to start. This is the default: the durable outbox row
    /// plus relay sweep recover any missed signals regardless.
    /// </summary>
    Warn = 1,

    /// <summary>
    /// Run the probe; throw at startup and fail the host start when compatibility cannot be verified. Use when the
    /// interceptor or diagnostic observer is the only signal path and a mis-wire must not go undetected.
    /// </summary>
    Strict = 2,
}
