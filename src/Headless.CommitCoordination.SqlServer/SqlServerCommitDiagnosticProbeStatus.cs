// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Outcome of the SQL Server commit diagnostic self-probe run at host startup, stored in
/// <see cref="SqlServerCommitDiagnosticProbeState" />.
/// </summary>
[PublicAPI]
public enum SqlServerCommitDiagnosticProbeStatus
{
    /// <summary>
    /// The startup hosted service has not run or has not yet completed. Initial state.
    /// </summary>
    NotRun = 0,

    /// <summary>
    /// The probe was skipped — either <see cref="SqlServerCommitDiagnosticProbeMode.Disabled" /> was set, or
    /// the mode is <see cref="SqlServerCommitDiagnosticProbeMode.Warn" /> and no connection factory was
    /// configured.
    /// </summary>
    Skipped = 1,

    /// <summary>
    /// The probe ran successfully and confirmed that SqlClient emits the diagnostic payloads required for
    /// out-of-band commit detection.
    /// </summary>
    Succeeded = 2,

    /// <summary>
    /// The probe ran but could not verify diagnostic compatibility — for example, the diagnostic event was not
    /// received within the configured timeout. The host started anyway because the mode was
    /// <see cref="SqlServerCommitDiagnosticProbeMode.Warn" />.
    /// </summary>
    Degraded = 3,

    /// <summary>
    /// The probe ran but could not verify diagnostic compatibility, and the mode was
    /// <see cref="SqlServerCommitDiagnosticProbeMode.Strict" />, so startup was aborted.
    /// </summary>
    Failed = 4,
}
