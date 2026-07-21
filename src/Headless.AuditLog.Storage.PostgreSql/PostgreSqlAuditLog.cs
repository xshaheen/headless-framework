// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.PostgreSql;

internal sealed class PostgreSqlAuditLog<TContext>(
    PostgreSqlAuditLogWriter writer,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICorrelationIdProvider correlationIdProvider,
    TimeProvider timeProvider,
    IOptions<AuditLogOptions> options
) : IAuditLog<TContext>
{
    public Task LogAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(request);

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
            Action = AuditLogFieldLimits.Truncate(request.Action, AuditLogFieldLimits.Action),
            EntityType = AuditLogFieldLimits.Truncate(request.EntityType, AuditLogFieldLimits.EntityType),
            EntityId = AuditLogFieldLimits.Truncate(request.EntityId, AuditLogFieldLimits.EntityId),
            NewValues = request.Data,
            Success = request.Success,
            ErrorCode = AuditLogFieldLimits.Truncate(request.ErrorCode, AuditLogFieldLimits.ErrorCode),
        };

        return writer.WriteAsync([entry], cancellationToken: cancellationToken);
    }
}
