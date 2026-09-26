// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Microsoft.Data.SqlClient;

namespace Headless.Idempotency.SqlServer;

/// <summary>Connection and command options for the SQL Server idempotency provider.</summary>
/// <remarks>
/// The schema that holds the record table is shared by every idempotency provider and configured through
/// <see cref="IdempotencyStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class SqlServerIdempotencyOptions
{
    /// <summary>
    /// Gets or sets the SqlClient connection string of the database that holds the idempotency records. It must also hold
    /// the fenced leases, because every admission, completion, and release writes its record and its lease in one
    /// transaction. Autonomous calls and the purge open their own connections with it, and an enlisted call is accepted
    /// only on a unit whose connection reaches the same database, so name the database explicitly
    /// (<c>Initial Catalog</c>). Required.
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

    internal SqlConnection CreateConnection()
    {
        return new(ConnectionString);
    }

    internal int CommandTimeoutSeconds => (int)Math.Ceiling(CommandTimeout.TotalSeconds);
}

internal sealed class SqlServerIdempotencyOptionsValidator : AbstractValidator<SqlServerIdempotencyOptions>
{
    public SqlServerIdempotencyOptionsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("A SQL Server connection string is required.");
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
    }
}

/// <summary>Checks the shared idempotency schema name against SQL Server's identifier rules.</summary>
internal sealed class SqlServerIdempotencyStorageOptionsValidator : AbstractValidator<IdempotencyStorageOptions>
{
    public SqlServerIdempotencyStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.SqlServer);
    }
}
