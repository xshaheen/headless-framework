// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog;

internal sealed class EfAuditLogStore(DbContext dbContext) : IAuditLogStore
{
    /// <inheritdoc />
    public IReadOnlyList<IAuditLogStoreEntry> Save(IReadOnlyList<AuditLogEntryData> entries) =>
        _AddEntries(entries, dbContext);

    /// <inheritdoc />
    public Task<IReadOnlyList<IAuditLogStoreEntry>> SaveAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(_AddEntries(entries, dbContext));
    }

    /// <inheritdoc />
    public IReadOnlyList<IAuditLogStoreEntry> Save(IReadOnlyList<AuditLogEntryData> entries, object savingContext)
    {
        return _AddEntries(entries, savingContext as DbContext ?? dbContext);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IAuditLogStoreEntry>> SaveAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        object savingContext,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(_AddEntries(entries, savingContext as DbContext ?? dbContext));
    }

    /// <inheritdoc />
    public void PrepareForRetry(object savingContext)
    {
        var context = savingContext as DbContext ?? dbContext;

        foreach (var entry in context.ChangeTracker.Entries<AuditLogEntry>().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static IReadOnlyList<IAuditLogStoreEntry> _AddEntries(
        IReadOnlyList<AuditLogEntryData> entries,
        DbContext context
    )
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var set = context.Set<AuditLogEntry>();
        var auditEntries = new List<IAuditLogStoreEntry>(entries.Count);

        foreach (var entry in entries)
        {
            var auditEntity = new AuditLogEntry
            {
                CreatedAt = entry.CreatedAt.UtcDateTime,
                UserId = _Truncate(entry.UserId, 128),
                AccountId = _Truncate(entry.AccountId, 128),
                TenantId = _Truncate(entry.TenantId, 128),
                IpAddress = _Truncate(entry.IpAddress, 45),
                UserAgent = _Truncate(entry.UserAgent, 512),
                CorrelationId = _Truncate(entry.CorrelationId, 128),
                Action = _Truncate(entry.Action, 256),
                ChangeType = entry.ChangeType,
                EntityType = _Truncate(entry.EntityType, 512),
                EntityId = _Truncate(entry.EntityId, 256),
                OldValues = entry.OldValues,
                NewValues = entry.NewValues,
                ChangedFields = entry.ChangedFields,
                Success = entry.Success,
                ErrorCode = _Truncate(entry.ErrorCode, 256),
            };

            set.Add(auditEntity);
            auditEntries.Add(new EfAuditLogStoreEntry(context, auditEntity));
        }
        // Do NOT call SaveChanges — entries commit atomically with the entity changes
        return auditEntries;
    }

    [return: NotNullIfNotNull(nameof(value))]
    private static string? _Truncate(string? value, int maxLength)
    {
        return value is { Length: var len } && len > maxLength ? value[..maxLength] : value;
    }

    private sealed class EfAuditLogStoreEntry(DbContext context, AuditLogEntry entity) : IAuditLogStoreEntry
    {
        public void Detach()
        {
            var entry = context.Entry(entity);

            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
