// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Data.SqlClient;

namespace Headless.AuditLog.SqlServer;

/// <summary>
/// Connection and command options for the SqlServer audit-log storage provider.
/// </summary>
[PublicAPI]
public sealed class SqlServerAuditLogOptions
{
    /// <summary>
    /// Microsoft.Data.SqlClient connection string used to open connections for DDL initialization
    /// and audit row writes. Required; validated non-empty on startup.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Default: 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal SqlConnection CreateConnection()
    {
        return new(ConnectionString);
    }
}

internal sealed class SqlServerAuditLogOptionsValidator : AbstractValidator<SqlServerAuditLogOptions>
{
    public SqlServerAuditLogOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
        RuleFor(x => x.CommandTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromSeconds(int.MaxValue));
    }
}
