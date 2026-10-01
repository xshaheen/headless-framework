// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog.SqlServer;

/// <summary>Connection and command options for the SQL Server audit-log storage provider.</summary>
[PublicAPI]
public sealed class SqlServerAuditLogOptions : RelationalAuditLogOptions;

internal sealed class SqlServerAuditLogOptionsValidator : RelationalAuditLogOptionsValidator<SqlServerAuditLogOptions>;
