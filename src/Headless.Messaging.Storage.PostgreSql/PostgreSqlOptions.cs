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

    /// <summary>
    /// Gets or sets a value indicating whether each durable consumer attempt runs in one database transaction that
    /// completes the inbox row, so writes the handler makes through <c>ConsumeContext.UnitOfWork</c> commit or roll
    /// back with it. Defaults to <see langword="true" />, which declares the <c>Transactional</c> inbox guarantee.
    /// </summary>
    /// <remarks>
    /// When <see langword="false" />, the storage declares the <c>Durable</c> guarantee, durable consumers need
    /// <c>MessagingOptions.MinimumInboxGuarantee = InboxGuarantee.Durable</c> to start, and they run with no unit of
    /// work. An attempt holds one pooled connection and an open transaction while its handler runs. An additional
    /// outbox (<c>AddOutbox().UsePostgreSql(...)</c>) ignores this setting.
    /// </remarks>
    public bool EnableTransactionalInbox { get; set; } = true;

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
