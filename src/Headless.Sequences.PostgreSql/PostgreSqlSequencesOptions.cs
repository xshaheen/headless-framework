// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Npgsql;

namespace Headless.Sequences.PostgreSql;

/// <summary>Connection, command, and table options for the PostgreSQL sequence provider.</summary>
[PublicAPI]
public sealed class PostgreSqlSequencesOptions
{
    /// <summary>The schema used when <see cref="Schema" /> is not set.</summary>
    public const string DefaultSchema = "sequences";

    /// <summary>The table used when <see cref="TableName" /> is not set.</summary>
    public const string DefaultTableName = "sequences";

    /// <summary>
    /// Gets or sets the Npgsql connection string of the database that holds the counters. Fast-mode calls open
    /// their own connections with it, and a gap-free call is accepted only on a unit whose connection reaches the
    /// same database. Required.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the timeout of every command this provider runs. Default: 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the schema that holds the counter table. Default: <c>sequences</c>.</summary>
    public string Schema { get; set; } = DefaultSchema;

    /// <summary>Gets or sets the name of the counter table. Default: <c>sequences</c>.</summary>
    public string TableName { get; set; } = DefaultTableName;

    /// <summary>
    /// Gets or sets whether the schema and table are created at host startup when missing. Default:
    /// <see langword="true" />. Set it to <see langword="false" /> when a migration tool owns the table; the provider
    /// never creates it lazily inside a call, because DDL inside a caller's transaction would roll back with it.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;

    internal NpgsqlConnection CreateConnection()
    {
        return new(ConnectionString);
    }

    internal int CommandTimeoutSeconds => (int)Math.Ceiling(CommandTimeout.TotalSeconds);
}

internal sealed class PostgreSqlSequencesOptionsValidator : AbstractValidator<PostgreSqlSequencesOptions>
{
    public PostgreSqlSequencesOptionsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .Must(static value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("A PostgreSQL connection string is required.");
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
        RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
        RuleFor(x => x.TableName).IsValidIdentifierFor(StorageProvider.PostgreSql);
    }
}
