// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// SQL Server commit diagnostic self-probe status.
/// </summary>
[PublicAPI]
public enum SqlServerCommitDiagnosticProbeStatus
{
    /// <summary>
    /// The probe has not run yet.
    /// </summary>
    NotRun = 0,

    /// <summary>
    /// The probe was explicitly skipped.
    /// </summary>
    Skipped = 1,

    /// <summary>
    /// The probe verified diagnostic compatibility.
    /// </summary>
    Succeeded = 2,

    /// <summary>
    /// The probe could not verify diagnostic compatibility, but startup continued.
    /// </summary>
    Degraded = 3,

    /// <summary>
    /// The probe failed in strict mode.
    /// </summary>
    Failed = 4,
}
