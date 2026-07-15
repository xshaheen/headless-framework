// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Npgsql;

namespace Headless.AuditLog.PostgreSql;

/// <summary>
/// Connection and command options for the PostgreSql audit-log storage provider.
/// </summary>
[PublicAPI]
public sealed class PostgreSqlAuditLogOptions
{
    /// <summary>
    /// Npgsql connection string used to open connections for DDL initialization and audit row writes.
    /// Required; validated non-empty on startup.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Default: 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal NpgsqlConnection CreateConnection()
    {
        return new(ConnectionString);
    }
}

internal sealed class PostgreSqlAuditLogOptionsValidator : AbstractValidator<PostgreSqlAuditLogOptions>
{
    public PostgreSqlAuditLogOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}
