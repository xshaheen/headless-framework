// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.Permissions.PostgreSql;

/// <summary>
/// Connection and command options for the PostgreSQL permissions storage provider.
/// </summary>
[PublicAPI]
public sealed class PostgreSqlPermissionsOptions
{
    /// <summary>The Npgsql connection string used to connect to the PostgreSQL database.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal NpgsqlConnection CreateConnection() => new(ConnectionString);
}

internal sealed class PostgreSqlPermissionsOptionsValidator : AbstractValidator<PostgreSqlPermissionsOptions>
{
    public PostgreSqlPermissionsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}
