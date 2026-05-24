// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.AuditLog.PostgreSql;

[PublicAPI]
public sealed class PostgreSqlAuditLogOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal NpgsqlConnection CreateConnection() => new(ConnectionString);
}

internal sealed class PostgreSqlAuditLogOptionsValidator : AbstractValidator<PostgreSqlAuditLogOptions>
{
    public PostgreSqlAuditLogOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
    }
}
