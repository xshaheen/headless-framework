// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.Settings.PostgreSql;

[PublicAPI]
public sealed class PostgreSqlSettingsOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public NpgsqlConnection CreateConnection() => new(ConnectionString);
}

internal sealed class PostgreSqlSettingsOptionsValidator : AbstractValidator<PostgreSqlSettingsOptions>
{
    public PostgreSqlSettingsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x));
    }
}
