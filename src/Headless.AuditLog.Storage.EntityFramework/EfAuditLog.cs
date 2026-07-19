// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

internal sealed class EfAuditLog<TContext>(
    TContext context,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICorrelationIdProvider correlationIdProvider,
    TimeProvider timeProvider,
    IOptions<AuditLogOptions> options
) : IAuditLog<TContext>
    where TContext : DbContext
{
    /// <inheritdoc />
    public Task LogAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!options.Value.IsEnabled)
        {
            return Task.CompletedTask;
        }

        context
            .Set<AuditLogEntry>()
            .Add(
                new AuditLogEntry
                {
                    CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
                    UserId = AuditLogFieldLimits.Truncate(currentUser.UserId?.ToString(), AuditLogFieldLimits.UserId),
                    AccountId = AuditLogFieldLimits.Truncate(
                        currentUser.AccountId?.ToString(),
                        AuditLogFieldLimits.AccountId
                    ),
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
                }
            );

        return Task.CompletedTask;
    }
}
