// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Carries the latest result of the SQL Server commit diagnostic self-probe, updated by the hosted service at
/// startup and queryable at runtime (e.g. from a health check).
/// </summary>
/// <remarks>
/// All property reads and writes are synchronized under a private lock. The object is registered as a singleton
/// so consumers can inject it to expose the probe result via a health check endpoint.
/// </remarks>
[PublicAPI]
public sealed class SqlServerCommitDiagnosticProbeState
{
    private readonly Lock _gate = new();

    /// <summary>
    /// Gets the outcome of the most recent self-probe run. Starts as
    /// <see cref="SqlServerCommitDiagnosticProbeStatus.NotRun" /> until the startup hosted service completes.
    /// </summary>
    public SqlServerCommitDiagnosticProbeStatus Status { get; private set; } =
        SqlServerCommitDiagnosticProbeStatus.NotRun;

    /// <summary>
    /// Gets a human-readable description of the most recent probe result, or <see langword="null" /> if the
    /// probe has not run yet.
    /// </summary>
    public string? Message { get; private set; }

    /// <summary>
    /// Gets the exception captured by the most recent probe run when the status is
    /// <see cref="SqlServerCommitDiagnosticProbeStatus.Degraded" /> or
    /// <see cref="SqlServerCommitDiagnosticProbeStatus.Failed" />; otherwise <see langword="null" />.
    /// </summary>
    public Exception? Exception { get; private set; }

    internal void MarkSkipped(string message)
    {
        _Set(SqlServerCommitDiagnosticProbeStatus.Skipped, message, exception: null);
    }

    internal void MarkSucceeded(string message)
    {
        _Set(SqlServerCommitDiagnosticProbeStatus.Succeeded, message, exception: null);
    }

    internal void MarkDegraded(string message, Exception? exception = null)
    {
        _Set(SqlServerCommitDiagnosticProbeStatus.Degraded, message, exception);
    }

    internal void MarkFailed(string message, Exception? exception = null)
    {
        _Set(SqlServerCommitDiagnosticProbeStatus.Failed, message, exception);
    }

    private void _Set(SqlServerCommitDiagnosticProbeStatus status, string message, Exception? exception)
    {
        lock (_gate)
        {
            Status = status;
            Message = message;
            Exception = exception;
        }
    }
}
