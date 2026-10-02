// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog.PostgreSql;

/// <summary>Connection and command options for the PostgreSql audit-log storage provider.</summary>
[PublicAPI]
public sealed class PostgreSqlAuditLogOptions : RelationalAuditLogOptions;

internal sealed class PostgreSqlAuditLogOptionsValidator
    : RelationalAuditLogOptionsValidator<PostgreSqlAuditLogOptions>;
