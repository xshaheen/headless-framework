// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Messaging.Persistence;
using Headless.Sql.SqlServer;
using Microsoft.Data.SqlClient;

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

    internal string Version { get; set; } = null!;

    /// <summary>Describes this database to the relational messaging storage.</summary>
    internal RelationalMessagingStorage ToStorage()
    {
        var connectionString = ConnectionString;

        return new RelationalMessagingStorage(
            SqlServerDialect.Instance,
            "SqlServer",
            () => new SqlConnection(connectionString),
            OwnerColumnMaxLength
        );
    }
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
    }
}
