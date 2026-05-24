// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.Settings.PostgreSql;

[PublicAPI]
public sealed class PostgreSqlSettingsOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal NpgsqlConnection CreateConnection() => new(ConnectionString);
}

internal sealed class PostgreSqlSettingsOptionsValidator : AbstractValidator<PostgreSqlSettingsOptions>
{
    public PostgreSqlSettingsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
    }
}
