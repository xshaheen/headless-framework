// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Persistence;
using Headless.Sql.PostgreSql;
using Npgsql;

namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL-specific configuration for the raw ADO.NET messaging storage backend. The schema that
/// holds the messaging tables is not here: it belongs to the feature, on
/// <see cref="Headless.Messaging.MessagingStorageOptions"/>.
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

    /// <summary>Describes this database to the relational messaging storage.</summary>
    internal RelationalMessagingStorage ToStorage()
    {
        return new RelationalMessagingStorage(
            PostgreSqlDialect.Instance,
            "PostgreSql",
            CreateConnection,
            OwnerColumnMaxLength
        );
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
    }
}
