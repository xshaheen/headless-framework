// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.AuditLog.SqlServer;

[PublicAPI]
public sealed class SqlServerAuditLogOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

internal sealed class SqlServerAuditLogOptionsValidator : AbstractValidator<SqlServerAuditLogOptions>
{
    public SqlServerAuditLogOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x));
    }
}
