// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Single source of truth for audit-log column length limits. Storage providers (raw PG/SqlServer
/// writers + the EF entity configuration) and runtime truncation call sites all reference these
/// constants so column DDL and runtime truncation can never drift apart.
/// </summary>
internal static class AuditLogFieldLimits
{
    public const int UserId = 128;
    public const int AccountId = 128;
    public const int TenantId = 128;
    public const int IpAddress = 45;
    public const int UserAgent = 512;
    public const int CorrelationId = 128;
    public const int Action = 256;
    public const int EntityType = 512;
    public const int EntityId = 256;
    public const int ErrorCode = 256;

    [return: NotNullIfNotNull(nameof(value))]
    public static string? Truncate(string? value, int maxLength)
    {
        return value.TruncateEnd(maxLength);
    }
}
