// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.Coordination.PostgreSql;

/// <summary>Options for the PostgreSQL coordination backing store.</summary>
[PublicAPI]
public sealed class PostgreSqlCoordinationOptions
{
    /// <summary>
    /// Npgsql connection string. Either this or <see cref="DataSource"/> must be provided; <see cref="DataSource"/>
    /// takes precedence when both are set.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Pre-configured <see cref="NpgsqlDataSource"/>. Takes precedence over <see cref="ConnectionString"/>
    /// when set. Use this to share a data source with connection pooling already configured.
    /// </summary>
    /// <remarks>
    /// This member is deliberately typed as the provider-native <see cref="NpgsqlDataSource"/> rather than an
    /// abstraction: the store relies on full-fidelity Npgsql behavior (pooling and connection configuration) that a
    /// generic <see cref="System.Data.Common.DbDataSource"/> cannot guarantee, and this package is Npgsql-specific by
    /// contract, so the coupling is intentional.
    /// </remarks>
    public NpgsqlDataSource? DataSource { get; set; }

    /// <summary>
    /// ADO.NET command timeout for all coordination store queries. Must be positive and at most 10 minutes.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When <see langword="true"/> (default), an <c>IHostedService</c> initializer creates the
    /// coordination schema and tables at startup if they do not already exist. Set to
    /// <see langword="false"/> if the schema is managed externally (for example by a migration tool).
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    internal NpgsqlConnection CreateConnection()
    {
        return DataSource is not null ? DataSource.CreateConnection() : new NpgsqlConnection(ConnectionString);
    }
}

internal sealed class PostgreSqlCoordinationOptionsValidator : AbstractValidator<PostgreSqlCoordinationOptions>
{
    public PostgreSqlCoordinationOptionsValidator()
    {
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromMinutes(10));
        RuleFor(x => x)
            .Must(x => x.DataSource is not null || !string.IsNullOrWhiteSpace(x.ConnectionString))
            .WithMessage(
                $"{nameof(PostgreSqlCoordinationOptions.ConnectionString)} or {nameof(PostgreSqlCoordinationOptions.DataSource)} is required."
            );
    }
}
