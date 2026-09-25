// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.MultiTenancy;

namespace Headless.AuditLog.Internal;

/// <summary>
/// Builds the row for an explicit audit event, shared by the enlisted <see cref="IAuditLog{TContext}"/> and the
/// standalone <see cref="IAuditLogWriter{TContext}"/> so both stamp the actor and truncate fields the same way.
/// </summary>
internal static class ExplicitAuditLogEntry
{
    public static AuditLogEntry Create(
        AuditLogWriteRequest request,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        ICorrelationIdProvider correlationIdProvider,
        TimeProvider timeProvider
    )
    {
        return new AuditLogEntry
        {
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            UserId = AuditLogFieldLimits.Truncate(currentUser.UserId?.ToString(), AuditLogFieldLimits.UserId),
            AccountId = AuditLogFieldLimits.Truncate(currentUser.AccountId?.ToString(), AuditLogFieldLimits.AccountId),
            TenantId = AuditLogFieldLimits.Truncate(currentTenant.Id, AuditLogFieldLimits.TenantId),
            CorrelationId = AuditLogFieldLimits.Truncate(
                correlationIdProvider.CorrelationId,
                AuditLogFieldLimits.CorrelationId
            ),
            Action = AuditLogFieldLimits.Truncate(request.Action, AuditLogFieldLimits.Action),
            ChangeType = null,
            EntityType = AuditLogFieldLimits.Truncate(request.EntityType, AuditLogFieldLimits.EntityType),
            EntityId = AuditLogFieldLimits.Truncate(request.EntityId, AuditLogFieldLimits.EntityId),
            NewValues = request.Data,
            Success = request.Success,
            ErrorCode = AuditLogFieldLimits.Truncate(request.ErrorCode, AuditLogFieldLimits.ErrorCode),
        };
    }
}
