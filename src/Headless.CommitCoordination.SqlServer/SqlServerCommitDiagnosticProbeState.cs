// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Captures the latest SQL Server commit diagnostic self-probe result.
/// </summary>
[PublicAPI]
public sealed class SqlServerCommitDiagnosticProbeState
{
    private readonly Lock _gate = new();

    /// <summary>
    /// Gets the latest probe status.
    /// </summary>
    public SqlServerCommitDiagnosticProbeStatus Status { get; private set; } =
        SqlServerCommitDiagnosticProbeStatus.NotRun;

    /// <summary>
    /// Gets the latest probe message.
    /// </summary>
    public string? Message { get; private set; }

    /// <summary>
    /// Gets the exception captured by the latest failed or degraded probe.
    /// </summary>
    public Exception? Exception { get; private set; }

    internal void MarkSkipped(string message) => _Set(SqlServerCommitDiagnosticProbeStatus.Skipped, message, null);

    internal void MarkSucceeded(string message) => _Set(SqlServerCommitDiagnosticProbeStatus.Succeeded, message, null);

    internal void MarkDegraded(string message, Exception? exception = null) =>
        _Set(SqlServerCommitDiagnosticProbeStatus.Degraded, message, exception);

    internal void MarkFailed(string message, Exception? exception = null) =>
        _Set(SqlServerCommitDiagnosticProbeStatus.Failed, message, exception);

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
