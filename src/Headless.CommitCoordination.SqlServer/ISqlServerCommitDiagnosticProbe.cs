// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.SqlServer;

internal interface ISqlServerCommitDiagnosticProbe
{
    ValueTask<SqlServerCommitDiagnosticProbeResult> ProbeAsync(CancellationToken cancellationToken);
}

