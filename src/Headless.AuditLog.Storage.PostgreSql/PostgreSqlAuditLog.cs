// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.PostgreSql;

internal sealed class PostgreSqlAuditLog<TContext>(
    PostgreSqlAuditLogWriter writer,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICorrelationIdProvider correlationIdProvider,
    IClock clock,
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
            CreatedAt = clock.UtcNow,
            UserId = _Truncate(currentUser.UserId?.ToString(), 128),
            AccountId = _Truncate(currentUser.AccountId?.ToString(), 128),
            TenantId = _Truncate(currentTenant.Id, 128),
            CorrelationId = _Truncate(correlationIdProvider.CorrelationId, 128),
            Action = _Truncate(action, 256),
            EntityType = _Truncate(entityType, 512),
            EntityId = _Truncate(entityId, 256),
            NewValues = data,
            Success = success,
            ErrorCode = _Truncate(errorCode, 256),
        };

        return writer.WriteAsync([entry], cancellationToken: cancellationToken);
    }

    [return: NotNullIfNotNull(nameof(value))]
    private static string? _Truncate(string? value, int maxLength) =>
        value is { Length: var len } && len > maxLength ? value[..maxLength] : value;
}
