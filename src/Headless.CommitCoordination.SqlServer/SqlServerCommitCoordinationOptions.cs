// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Data.SqlClient;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// SQL Server commit coordination options.
/// </summary>
[PublicAPI]
public sealed class SqlServerCommitCoordinationOptions
{
    /// <summary>
    /// Controls how startup handles the SQL Server diagnostic self-probe.
    /// </summary>
    public SqlServerCommitDiagnosticProbeMode DiagnosticProbeMode { get; set; } =
        SqlServerCommitDiagnosticProbeMode.Warn;

    /// <summary>
    /// Creates a connection used by the startup probe to verify that SqlClient still emits the diagnostic payloads
    /// required by out-of-band commit detection. Leave unset to skip the live transaction probe in warn mode.
    /// </summary>
    public Func<CancellationToken, ValueTask<SqlConnection>>? DiagnosticProbeConnectionFactory { get; set; }

    /// <summary>
    /// Bounds the startup diagnostic self-probe.
    /// </summary>
    public TimeSpan DiagnosticProbeTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

internal sealed class SqlServerCommitCoordinationOptionsValidator
    : AbstractValidator<SqlServerCommitCoordinationOptions>
{
    public SqlServerCommitCoordinationOptionsValidator()
    {
        // A non-positive timeout makes the probe CancellationTokenSource fire immediately, so the probe always
        // "times out" — in Strict mode that fails startup, in Warn mode it permanently degrades health. Catch the
        // misconfiguration at startup instead.
        RuleFor(x => x.DiagnosticProbeTimeout).GreaterThan(TimeSpan.Zero);
    }
}
