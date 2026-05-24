// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.AuditLog;

[PublicAPI]
public sealed class AuditLogStorageOptions
{
    public string? Schema { get; set; }

    public string TableName { get; set; } = "audit_log";

    public string? JsonColumnType { get; set; }
}

internal sealed class AuditLogStorageOptionsValidator : AbstractValidator<AuditLogStorageOptions>
{
    public AuditLogStorageOptionsValidator()
    {
        RuleFor(x => x.TableName).NotEmpty().Must(x => !string.IsNullOrWhiteSpace(x));
    }
}
