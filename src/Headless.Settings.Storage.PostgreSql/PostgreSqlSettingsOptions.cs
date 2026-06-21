// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.Settings.PostgreSql;

/// <summary>Options for the PostgreSQL settings storage provider.</summary>
[PublicAPI]
public sealed class PostgreSqlSettingsOptions
{
    /// <summary>PostgreSQL connection string used to open connections for DDL and DML operations.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal NpgsqlConnection CreateConnection() => new(ConnectionString);
}

/// <summary>Validates <see cref="PostgreSqlSettingsOptions"/> on startup.</summary>
internal sealed class PostgreSqlSettingsOptionsValidator : AbstractValidator<PostgreSqlSettingsOptions>
{
    public PostgreSqlSettingsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}
