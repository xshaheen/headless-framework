// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.SqlServer;

internal readonly record struct SqlServerCommitDiagnosticProbeResult(
    bool Succeeded,
    string Message,
    Exception? Exception = null
)
{
    public static SqlServerCommitDiagnosticProbeResult Success(string message)
    {
        return new(Succeeded: true, message);
    }

    public static SqlServerCommitDiagnosticProbeResult Failure(string message, Exception? exception = null)
    {
        return new(Succeeded: false, message, exception);
    }
}
