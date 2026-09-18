// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.SqlServer;

/// <summary>
/// SQL Server-specific configuration for the raw ADO.NET messaging storage backend. The schema that
/// holds the messaging tables is not here: it belongs to the feature, on
/// <see cref="Headless.Messaging.Configuration.MessagingStorageOptions"/>.
/// </summary>
[PublicAPI]
public sealed class SqlServerOptions
{
    /// <summary>Gets or sets the maximum length for the Owner column.</summary>
    public int OwnerColumnMaxLength { get; set; } = DataStorageConstants.OwnerColumnMaxLength;

    /// <summary>
    /// Gets or sets the database's connection string that will be used to store database entities.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the command timeout applied to schema-initialization DDL that can scale with table
    /// size — the history-table index builds (<c>InboxOperationReceipts</c>, <c>InboxAudit</c>) and the
    /// <c>sp_getapplock</c> wait that serializes initializers across replicas.
    /// <para>
    /// History tables already exist and grow without bound on upgraded schemas, so adding an index to them
    /// is an offline build over the full backlog that can run far longer than the OLTP
    /// <c>MessagingOptions.CommandTimeout</c> (~30s) used for query/write paths.
    /// </para>
    /// <para>
    /// Default <see langword="null" /> means <b>no timeout</b> (wait indefinitely): the DDL runs with a
    /// SqlClient <c>CommandTimeout</c> of <c>0</c>. Set a finite value to cap startup DDL. <see cref="TimeSpan.Zero"/>
    /// is also treated as "no timeout". A negative value is rejected at validation time.
    /// </para>
    /// </summary>
    public TimeSpan? DdlCommandTimeout { get; set; }

    internal string Version { get; set; } = null!;
}

internal sealed class SqlServerOptionsValidator : AbstractValidator<SqlServerOptions>
{
    public SqlServerOptionsValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage(
                "SQL Server messaging storage requires a ConnectionString. "
                    + "Configure via UseSqlServer(connectionString) or UseSqlServer(options => options.ConnectionString = ...)"
            );

        RuleFor(x => x.OwnerColumnMaxLength).GreaterThanOrEqualTo(DataStorageConstants.MinimumOwnerColumnMaxLength);

        // A negative DDL timeout is meaningless; null/Zero already express "no timeout".
        RuleFor(x => x.DdlCommandTimeout!.Value)
            .GreaterThanOrEqualTo(TimeSpan.Zero)
            .When(x => x.DdlCommandTimeout is not null)
            .WithMessage("DdlCommandTimeout must be greater than or equal to zero (zero or null means no timeout).");
    }
}
