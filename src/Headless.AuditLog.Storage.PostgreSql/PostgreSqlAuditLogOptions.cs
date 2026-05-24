// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.AuditLog.PostgreSql;

[PublicAPI]
public sealed class PostgreSqlAuditLogOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public NpgsqlConnection CreateConnection() => new(ConnectionString);
}

internal sealed class PostgreSqlAuditLogOptionsValidator : AbstractValidator<PostgreSqlAuditLogOptions>
{
    public PostgreSqlAuditLogOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x));
    }
}
