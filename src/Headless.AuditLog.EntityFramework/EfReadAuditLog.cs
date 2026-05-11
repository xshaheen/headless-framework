// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog;

internal sealed class EfReadAuditLog<TContext>(TContext context) : IReadAuditLog<TContext>
    where TContext : DbContext
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLogEntryData>> QueryAsync(
        string? action = null,
        string? entityType = null,
        string? entityId = null,
        string? userId = null,
        string? tenantId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 100,
        CancellationToken cancellationToken = default
    )
    {
        var query = context.Set<AuditLogEntry>().AsNoTracking().AsQueryable();

        if (action is not null)
        {
            query = query.Where(e => e.Action == action);
        }

        if (entityType is not null)
        {
            query = query.Where(e => e.EntityType == entityType);
        }

        if (entityId is not null)
        {
            query = query.Where(e => e.EntityId == entityId);
        }

        if (userId is not null)
        {
            query = query.Where(e => e.UserId == userId);
        }

        if (tenantId is not null)
        {
            query = query.Where(e => e.TenantId == tenantId);
        }

        if (from is not null)
        {
            query = query.Where(e => e.CreatedAt >= from.Value.UtcDateTime);
        }

        if (to is not null)
        {
            query = query.Where(e => e.CreatedAt < to.Value.UtcDateTime);
        }

        var entries = await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entries.ConvertAll(e => new AuditLogEntryData
        {
            UserId = e.UserId,
            AccountId = e.AccountId,
            TenantId = e.TenantId,
            IpAddress = e.IpAddress,
            UserAgent = e.UserAgent,
            CorrelationId = e.CorrelationId,
            Action = e.Action,
            ChangeType = e.ChangeType,
            EntityType = e.EntityType,
            EntityId = e.EntityId,
            OldValues = e.OldValues,
            NewValues = e.NewValues,
            ChangedFields = e.ChangedFields,
            Success = e.Success,
            ErrorCode = e.ErrorCode,
            CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc)),
        });
    }
}
