// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog;

internal sealed class EfAuditLogStore(DbContext dbContext) : IAuditLogStore
{
    /// <inheritdoc />
    public void Save(IReadOnlyList<AuditLogEntryData> entries) => _AddEntries(entries);

    /// <inheritdoc />
    public Task SaveAsync(IReadOnlyList<AuditLogEntryData> entries, CancellationToken cancellationToken = default)
    {
        _AddEntries(entries);
        return Task.CompletedTask;
    }

    private void _AddEntries(IReadOnlyList<AuditLogEntryData> entries)
    {
        if (entries.Count == 0) return;

        var set = dbContext.Set<AuditLogEntry>();

        foreach (var entry in entries)
        {
            set.Add(new AuditLogEntry
            {
                CreatedAt = entry.CreatedAt,
                UserId = entry.UserId,
                AccountId = entry.AccountId,
                TenantId = entry.TenantId,
                IpAddress = entry.IpAddress,
                UserAgent = entry.UserAgent,
                CorrelationId = entry.CorrelationId,
                Action = entry.Action,
                ChangeType = entry.ChangeType,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                OldValues = entry.OldValues,
                NewValues = entry.NewValues,
                ChangedFields = entry.ChangedFields,
                Success = entry.Success,
                ErrorCode = entry.ErrorCode,
            });
        }
        // Do NOT call SaveChanges — entries commit atomically with the entity changes
    }
}
