// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

internal sealed class EfAuditLog(
    DbContext dbContext,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICorrelationIdProvider correlationIdProvider,
    IClock clock,
    IOptions<AuditLogOptions> options
) : IAuditLog
{
    /// <inheritdoc />
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
        if (!options.Value.IsEnabled) return Task.CompletedTask;

        dbContext.Set<AuditLogEntry>().Add(new AuditLogEntry
        {
            CreatedAt = clock.UtcNow,
            UserId = currentUser.UserId?.ToString(),
            AccountId = currentUser.AccountId?.ToString(),
            TenantId = currentTenant.Id,
            CorrelationId = correlationIdProvider.CorrelationId,
            Action = action,
            ChangeType = null,
            EntityType = entityType,
            EntityId = entityId,
            NewValues = data,
            Success = success,
            ErrorCode = errorCode,
        });

        return Task.CompletedTask;
    }
}
