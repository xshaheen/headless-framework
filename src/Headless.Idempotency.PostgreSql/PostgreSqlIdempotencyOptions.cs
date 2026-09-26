// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Npgsql;

namespace Headless.Idempotency.PostgreSql;

/// <summary>Connection and command options for the PostgreSQL idempotency provider.</summary>
/// <remarks>
/// The schema that holds the record table is shared by every idempotency provider and configured through
/// <see cref="IdempotencyStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class PostgreSqlIdempotencyOptions
{
    /// <summary>
    /// Gets or sets the Npgsql connection string of the database that holds the idempotency records. Autonomous calls,
    /// renewals, and the purge open their own connections with it, and an enlisted call is accepted only on a unit whose
    /// connection reaches the same database. Required.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timeout of every command this provider runs. Default: 30 seconds. An admission waits for
    /// whichever transaction holds the key's record, so this also bounds how long a concurrent admission of the same
    /// key waits behind an open enlisted admission, fence, or completion.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether the schema, record table, and index are created at host startup when missing. Default:
    /// <see langword="true" />. Set it to <see langword="false" /> when a migration tool owns them; the provider never
    /// creates them lazily inside a call, because DDL inside a caller's transaction would roll back with it.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    internal NpgsqlConnection CreateConnection()
    {
        return new(ConnectionString);
    }

    internal int CommandTimeoutSeconds => (int)Math.Ceiling(CommandTimeout.TotalSeconds);
}

internal sealed class PostgreSqlIdempotencyOptionsValidator : AbstractValidator<PostgreSqlIdempotencyOptions>
{
    public PostgreSqlIdempotencyOptionsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("A PostgreSQL connection string is required.");
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
    }
}

/// <summary>Checks the shared idempotency schema name against PostgreSQL's identifier rules.</summary>
internal sealed class PostgreSqlIdempotencyStorageOptionsValidator : AbstractValidator<IdempotencyStorageOptions>
{
    public PostgreSqlIdempotencyStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
