// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Npgsql;

namespace Headless.Fencing.PostgreSql;

/// <summary>Connection and command options for the PostgreSQL fencing provider.</summary>
/// <remarks>
/// The schema that holds the lease table is shared by every fencing provider and configured through
/// <see cref="FencingStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class PostgreSqlFencingOptions
{
    /// <summary>
    /// Gets or sets the Npgsql connection string of the database that holds the leases. Autonomous calls and sweeps
    /// open their own connections with it, and an enlisted call is accepted only on a unit whose connection reaches
    /// the same database. Required.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timeout of every command this provider runs. Default: 30 seconds. A fence read waits for
    /// whichever transaction holds the lease row, so this also bounds how long a grant waits behind an open fence.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether the schema, lease table, indexes, and generation sequence are created at host startup
    /// when missing. Default: <see langword="true" />. Set it to <see langword="false" /> when a migration tool owns
    /// them; the provider never creates them lazily inside a call, because DDL inside a caller's transaction would
    /// roll back with it.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    internal NpgsqlConnection CreateConnection()
    {
        return new(ConnectionString);
    }

    internal int CommandTimeoutSeconds => (int)Math.Ceiling(CommandTimeout.TotalSeconds);
}

internal sealed class PostgreSqlFencingOptionsValidator : AbstractValidator<PostgreSqlFencingOptions>
{
    public PostgreSqlFencingOptionsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("A PostgreSQL connection string is required.");
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
    }
}

/// <summary>Checks the shared fencing schema name against PostgreSQL's identifier rules.</summary>
internal sealed class PostgreSqlFencingStorageOptionsValidator : AbstractValidator<FencingStorageOptions>
{
    public PostgreSqlFencingStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
