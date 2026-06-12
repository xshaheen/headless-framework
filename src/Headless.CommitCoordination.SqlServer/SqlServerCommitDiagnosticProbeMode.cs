// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Controls SQL Server diagnostic self-probe startup behavior.
/// </summary>
[PublicAPI]
public enum SqlServerCommitDiagnosticProbeMode
{
    /// <summary>
    /// Do not run the diagnostic self-probe.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Record a degraded probe state and log a warning when the probe cannot prove diagnostic compatibility.
    /// </summary>
    Warn = 1,

    /// <summary>
    /// Fail hosted-service startup when the probe cannot prove diagnostic compatibility.
    /// </summary>
    Strict = 2,
}

