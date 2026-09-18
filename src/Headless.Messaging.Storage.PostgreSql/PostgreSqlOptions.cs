// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Persistence;
using Npgsql;

namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL-specific configuration for the raw ADO.NET messaging storage backend. The schema that
/// holds the messaging tables is not here: it belongs to the feature, on
/// <see cref="Headless.Messaging.Configuration.MessagingStorageOptions"/>.
/// </summary>
[PublicAPI]
public sealed class PostgreSqlOptions
{
    /// <summary>Gets or sets the maximum length for the Owner column.</summary>
    public int OwnerColumnMaxLength { get; set; } = DataStorageConstants.OwnerColumnMaxLength;

    /// <summary>
    /// Gets or sets the database's connection string that will be used to store database entities.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the Npgsql data source that will be used to store database entities.
    /// </summary>
    public NpgsqlDataSource? DataSource { get; set; }

    /// <summary>
    /// Gets or sets the command timeout applied to schema-initialization DDL — the
    /// <c>CREATE INDEX CONCURRENTLY</c> / <c>DROP INDEX CONCURRENTLY</c> builds, the
    /// <c>CREATE EXTENSION</c> probe, and the advisory-lock waits that gate them.
    /// <para>
    /// These operations can legitimately run for minutes-to-hours on a large table, far longer than
    /// the OLTP <c>MessagingOptions.CommandTimeout</c> (~30s) used for query/write paths. On timeout
    /// PostgreSQL marks a <c>CONCURRENTLY</c> index <c>INVALID</c> and the next boot must repair it, so
    /// this value is deliberately decoupled from the OLTP budget.
    /// </para>
    /// <para>
    /// Default <see langword="null" /> means <b>no timeout</b> (wait indefinitely): the DDL runs with an
    /// Npgsql <c>CommandTimeout</c> of <c>0</c>. Set a finite value to cap startup DDL. <see cref="TimeSpan.Zero"/>
    /// is also treated as "no timeout". A negative value is rejected at validation time.
    /// </para>
    /// </summary>
    public TimeSpan? DdlCommandTimeout { get; set; }

    internal string Version { get; set; } = null!;

    /// <summary>
    /// Creates an Npgsql connection from the configured data source.
    /// </summary>
    internal NpgsqlConnection CreateConnection()
    {
        if (DataSource is not null)
        {
            return DataSource.CreateConnection();
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL messaging storage requires either a DataSource or ConnectionString. "
                    + "Configure via UsePostgreSql(connectionString) or UsePostgreSql(options => options.ConnectionString = ...)"
            );
        }

        return new NpgsqlConnection(ConnectionString);
    }
}

internal sealed class PostgreSqlOptionsValidator : AbstractValidator<PostgreSqlOptions>
{
    public PostgreSqlOptionsValidator()
    {
        RuleFor(x => x)
            .Must(x => x.DataSource is not null || !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage(
                "PostgreSQL messaging storage requires either a DataSource or ConnectionString. "
                    + "Configure via UsePostgreSql(connectionString) or UsePostgreSql(options => options.ConnectionString = ...)"
            );

        RuleFor(x => x.OwnerColumnMaxLength).GreaterThanOrEqualTo(DataStorageConstants.MinimumOwnerColumnMaxLength);

        // A negative DDL timeout is meaningless; null/Zero already express "no timeout".
        RuleFor(x => x.DdlCommandTimeout!.Value)
            .GreaterThanOrEqualTo(TimeSpan.Zero)
            .When(x => x.DdlCommandTimeout is not null)
            .WithMessage("DdlCommandTimeout must be greater than or equal to zero (zero or null means no timeout).");
    }
}
