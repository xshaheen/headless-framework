// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog;

internal sealed class EfReadAuditLog<TContext>(IDbContextFactory<TContext> dbFactory) : IReadAuditLog<TContext>
    where TContext : DbContext
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLogEntryData>> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsPositive(query.Limit, "The query limit must be positive.", nameof(query));
        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entriesQuery = context.Set<AuditLogEntry>().AsNoTracking().AsQueryable();

        if (query.Action is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.Action == query.Action);
        }

        if (query.EntityType is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.EntityType == query.EntityType);
        }

        if (query.EntityId is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.EntityId == query.EntityId);
        }

        if (query.UserId is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.UserId == query.UserId);
        }

        if (query.TenantId is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.TenantId == query.TenantId);
        }

        if (query.From is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.CreatedAt >= query.From.Value.UtcDateTime);
        }

        if (query.To is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.CreatedAt < query.To.Value.UtcDateTime);
        }

        var entries = await entriesQuery
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(query.Limit)
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
