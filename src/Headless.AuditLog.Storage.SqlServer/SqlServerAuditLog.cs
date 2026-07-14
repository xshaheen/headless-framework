// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.SqlServer;

internal sealed class SqlServerAuditLog<TContext>(
    SqlServerAuditLogWriter writer,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICorrelationIdProvider correlationIdProvider,
    TimeProvider timeProvider,
    IOptions<AuditLogOptions> options
) : IAuditLog<TContext>
{
    public Task LogAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        Dictionary<string, object?>? data = null,
        bool success = true,
        string? errorCode = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!options.Value.IsEnabled)
        {
            return Task.CompletedTask;
        }

        var entry = new AuditLogEntryData
        {
            CreatedAt = timeProvider.GetUtcNow(),
            UserId = AuditLogFieldLimits.Truncate(currentUser.UserId?.ToString(), AuditLogFieldLimits.UserId),
            AccountId = AuditLogFieldLimits.Truncate(currentUser.AccountId?.ToString(), AuditLogFieldLimits.AccountId),
            TenantId = AuditLogFieldLimits.Truncate(currentTenant.Id, AuditLogFieldLimits.TenantId),
            CorrelationId = AuditLogFieldLimits.Truncate(
                correlationIdProvider.CorrelationId,
                AuditLogFieldLimits.CorrelationId
            ),
            Action = AuditLogFieldLimits.Truncate(action, AuditLogFieldLimits.Action),
            EntityType = AuditLogFieldLimits.Truncate(entityType, AuditLogFieldLimits.EntityType),
            EntityId = AuditLogFieldLimits.Truncate(entityId, AuditLogFieldLimits.EntityId),
            NewValues = data,
            Success = success,
            ErrorCode = AuditLogFieldLimits.Truncate(errorCode, AuditLogFieldLimits.ErrorCode),
        };

        return writer.WriteAsync([entry], cancellationToken: cancellationToken);
    }
}
