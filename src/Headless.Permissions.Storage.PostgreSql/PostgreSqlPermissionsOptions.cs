// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.Permissions.PostgreSql;

[PublicAPI]
public sealed class PostgreSqlPermissionsOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public NpgsqlConnection CreateConnection() => new(ConnectionString);
}

internal sealed class PostgreSqlPermissionsOptionsValidator : AbstractValidator<PostgreSqlPermissionsOptions>
{
    public PostgreSqlPermissionsOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x));
    }
}
