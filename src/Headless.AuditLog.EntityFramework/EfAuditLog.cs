// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
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
        {
            return Task.CompletedTask;
        }

        dbContext
            .Set<AuditLogEntry>()
            .Add(
                new AuditLogEntry
                {
                    CreatedAt = clock.UtcNow,
                    UserId = _Truncate(currentUser.UserId?.ToString(), 128),
                    AccountId = _Truncate(currentUser.AccountId?.ToString(), 128),
                    TenantId = _Truncate(currentTenant.Id, 128),
                    CorrelationId = _Truncate(correlationIdProvider.CorrelationId, 128),
                    Action = _Truncate(action, 256),
                    ChangeType = null,
                    EntityType = _Truncate(entityType, 512),
                    EntityId = _Truncate(entityId, 256),
                    NewValues = data,
                    Success = success,
                    ErrorCode = _Truncate(errorCode, 256),
                }
            );

        return Task.CompletedTask;
    }

    [return: NotNullIfNotNull(nameof(value))]
    private static string? _Truncate(string? value, int maxLength)
    {
        return value is { Length: var len } && len > maxLength ? value[..maxLength] : value;
    }
}
