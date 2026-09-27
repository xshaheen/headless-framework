// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog;

internal sealed class EfReadAuditLog<TContext>(IDbContextFactory<TContext> dbFactory) : IReadAuditLog<TContext>
    where TContext : DbContext
{
    /// <inheritdoc />
    public async Task<ContinuationPage<AuditLogEntryData>> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var position = AuditLogPaging.Validate(query);
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

        if (query.AccountId is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.AccountId == query.AccountId);
        }

        if (query.TenantId is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.TenantId == query.TenantId);
        }

        if (query.CorrelationId is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.CorrelationId == query.CorrelationId);
        }

        if (query.From is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.CreatedAt >= query.From.Value.UtcDateTime);
        }

        if (query.To is not null)
        {
            entriesQuery = entriesQuery.Where(e => e.CreatedAt < query.To.Value.UtcDateTime);
        }

        var newestFirst = query.Direction == AuditLogSortDirection.NewestFirst;

        if (position is { } after)
        {
            var (createdAt, id) = after;
            entriesQuery = newestFirst
                ? entriesQuery.Where(e => e.CreatedAt < createdAt || (e.CreatedAt == createdAt && e.Id < id))
                : entriesQuery.Where(e => e.CreatedAt > createdAt || (e.CreatedAt == createdAt && e.Id > id));
        }

        var ordered = newestFirst
            ? entriesQuery.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            : entriesQuery.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id);

        // One extra row tells whether another page exists without a second count query.
        var entries = await ordered.Take(query.Size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        string? continuationToken = null;

        if (entries.Count > query.Size)
        {
            entries.RemoveAt(entries.Count - 1);
            var last = entries[^1];
            continuationToken = AuditLogPaging.Encode(last.CreatedAt, last.Id);
        }

        var items = entries.ConvertAll(e => new AuditLogEntryData
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

        return new ContinuationPage<AuditLogEntryData>(items, query.Size, continuationToken);
    }
}
