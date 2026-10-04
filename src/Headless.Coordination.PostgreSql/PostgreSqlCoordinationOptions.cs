// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.Coordination.PostgreSql;

/// <summary>Connection and command options for the PostgreSQL coordination backing store.</summary>
/// <remarks>
/// Either <see cref="RelationalCoordinationOptions.ConnectionString" /> or <see cref="DataSource" /> must be provided;
/// <see cref="DataSource" /> takes precedence when both are set. The schema that holds the membership tables is
/// configured through <see cref="CoordinationStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class PostgreSqlCoordinationOptions : RelationalCoordinationOptions
{
    /// <summary>
    /// Gets or sets a pre-configured <see cref="NpgsqlDataSource"/>. Takes precedence over
    /// <see cref="RelationalCoordinationOptions.ConnectionString"/> when set. Use this to share a data source with
    /// connection pooling already configured.
    /// </summary>
    /// <remarks>
    /// This member is deliberately typed as the provider-native <see cref="NpgsqlDataSource"/> rather than an
    /// abstraction: the store relies on full-fidelity Npgsql behavior (pooling and connection configuration) that a
    /// generic <see cref="System.Data.Common.DbDataSource"/> cannot guarantee, and this package is Npgsql-specific by
    /// contract, so the coupling is intentional.
    /// </remarks>
    public NpgsqlDataSource? DataSource { get; set; }

    internal NpgsqlConnection CreateConnection()
    {
        return DataSource is not null ? DataSource.CreateConnection() : new NpgsqlConnection(ConnectionString);
    }
}

internal sealed class PostgreSqlCoordinationOptionsValidator
    : RelationalCoordinationOptionsValidator<PostgreSqlCoordinationOptions>
{
    public PostgreSqlCoordinationOptionsValidator()
    {
        RuleFor(x => x)
            .Must(x => x.DataSource is not null || !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage(
                $"{nameof(PostgreSqlCoordinationOptions.ConnectionString)} or {nameof(PostgreSqlCoordinationOptions.DataSource)} is required."
            );
    }
}

/// <summary>Checks the shared coordination schema name against PostgreSQL's identifier rules.</summary>
internal sealed class PostgreSqlCoordinationStorageOptionsValidator : AbstractValidator<CoordinationStorageOptions>
{
    public PostgreSqlCoordinationStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
