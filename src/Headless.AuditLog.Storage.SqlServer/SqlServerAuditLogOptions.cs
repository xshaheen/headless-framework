// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.AuditLog.SqlServer;

[PublicAPI]
public sealed class SqlServerAuditLogOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Timeout applied to DDL/DML commands issued by this provider. Defaults to 30 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed class SqlServerAuditLogOptionsValidator : AbstractValidator<SqlServerAuditLogOptions>
{
    public SqlServerAuditLogOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();
    }
}
