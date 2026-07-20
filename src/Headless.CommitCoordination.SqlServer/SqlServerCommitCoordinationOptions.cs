// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Data.SqlClient;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Configuration options for the SQL Server commit coordination diagnostic self-probe.
/// </summary>
/// <remarks>
/// SQL Server out-of-band commit detection relies on SqlClient's diagnostic listener
/// (<c>Microsoft.Data.SqlClient</c>) emitting commit/rollback events. The startup self-probe verifies this
/// behavior at host start by opening a temporary transaction, committing it, and checking whether the diagnostic
/// event was received. Configure <see cref="DiagnosticProbeMode" /> to control how a mis-wire is handled.
/// </remarks>
[PublicAPI]
public sealed class SqlServerCommitCoordinationOptions
{
    /// <summary>
    /// Gets or sets how the startup diagnostic self-probe reacts when it cannot verify that SqlClient emits the
    /// required diagnostic payloads. Defaults to <see cref="CommitProbeMode.Warn" />.
    /// </summary>
    public CommitProbeMode DiagnosticProbeMode { get; set; } = CommitProbeMode.Warn;

    /// <summary>
    /// Gets or sets a factory that creates the <see cref="SqlConnection" /> used by the startup probe to run a
    /// live transaction commit and verify that SqlClient emits the required diagnostic payloads. When
    /// <see langword="null" /> and the mode is <see cref="CommitProbeMode.Warn" />, the
    /// live transaction check is skipped and the probe records a <see cref="SqlServerCommitDiagnosticProbeStatus.Skipped" /> result.
    /// </summary>
    /// <remarks>
    /// This member is deliberately typed as the provider-native <see cref="SqlConnection" /> rather than
    /// <see cref="System.Data.Common.DbConnection" />: the probe exists precisely to validate
    /// <c>Microsoft.Data.SqlClient</c>'s diagnostic-listener behavior, which only fires for genuine
    /// <see cref="SqlConnection" /> transactions, so the SqlClient coupling is intentional.
    /// </remarks>
    public Func<CancellationToken, ValueTask<SqlConnection>>? DiagnosticProbeConnectionFactory { get; set; }

    /// <summary>
    /// Gets or sets the maximum duration allowed for the startup diagnostic self-probe. Must be positive.
    /// Defaults to 5 seconds.
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
