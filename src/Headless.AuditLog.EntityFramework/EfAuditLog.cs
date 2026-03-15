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
        if (!options.Value.IsEnabled)
            return Task.CompletedTask;

        dbContext
            .Set<AuditLogEntry>()
            .Add(
                new AuditLogEntry
                {
                    CreatedAt = clock.UtcNow,
                    UserId = Truncate(currentUser.UserId?.ToString(), 128),
                    AccountId = Truncate(currentUser.AccountId?.ToString(), 128),
                    TenantId = Truncate(currentTenant.Id, 128),
                    CorrelationId = Truncate(correlationIdProvider.CorrelationId, 128),
                    Action = Truncate(action, 256)!,
                    ChangeType = null,
                    EntityType = Truncate(entityType, 512),
                    EntityId = Truncate(entityId, 256),
                    NewValues = data,
                    Success = success,
                    ErrorCode = Truncate(errorCode, 256),
                }
            );

        return Task.CompletedTask;
    }

    private static string? Truncate(string? value, int maxLength)
        => value is { Length: var len } && len > maxLength ? value[..maxLength] : value;
}
