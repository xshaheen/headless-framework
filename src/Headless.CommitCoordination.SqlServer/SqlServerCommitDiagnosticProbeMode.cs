// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Controls how the SQL Server commit coordination startup self-probe reacts when it cannot verify that
/// SqlClient is emitting the diagnostic payloads required for out-of-band commit detection.
/// </summary>
[PublicAPI]
public enum SqlServerCommitDiagnosticProbeMode
{
    /// <summary>
    /// Skip the self-probe entirely. Use when the host cannot or should not connect to SQL Server at startup
    /// (e.g. cold-start latency budgets or environments where the database is not reachable at boot). The
    /// trade-off is losing early mis-wire detection; a diagnostic observer that is not firing stays silent
    /// until a real transaction commit relies on it and the relay sweep recovers the work instead.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Run the probe; log a warning and record <see cref="SqlServerCommitDiagnosticProbeStatus.Degraded" />
    /// when compatibility cannot be verified, but allow the host to start. This is the default. The durable
    /// outbox row plus relay sweep recover any missed signals regardless.
    /// </summary>
    Warn = 1,

    /// <summary>
    /// Run the probe; throw at hosted-service startup and record
    /// <see cref="SqlServerCommitDiagnosticProbeStatus.Failed" /> when compatibility cannot be verified,
    /// preventing the host from starting. Use when the diagnostic observer is the only signal path and a
    /// mis-wire must not go undetected.
    /// </summary>
    Strict = 2,
}
