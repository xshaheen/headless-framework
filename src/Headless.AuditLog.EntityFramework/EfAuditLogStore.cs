// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog;

internal sealed class EfAuditLogStore(DbContext dbContext) : IAuditLogStore
{
    /// <inheritdoc />
    public void Save(IReadOnlyList<AuditLogEntryData> entries) => _AddEntries(entries, dbContext);

    /// <inheritdoc />
    public Task SaveAsync(IReadOnlyList<AuditLogEntryData> entries, CancellationToken cancellationToken = default)
    {
        _AddEntries(entries, dbContext);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Save(IReadOnlyList<AuditLogEntryData> entries, object savingContext)
    {
        _AddEntries(entries, savingContext as DbContext ?? dbContext);
    }

    /// <inheritdoc />
    public Task SaveAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        object savingContext,
        CancellationToken cancellationToken = default
    )
    {
        _AddEntries(entries, savingContext as DbContext ?? dbContext);
        return Task.CompletedTask;
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

    private static void _AddEntries(IReadOnlyList<AuditLogEntryData> entries, DbContext context)
    {
        if (entries.Count == 0)
            return;

        var set = context.Set<AuditLogEntry>();

        foreach (var entry in entries)
        {
            set.Add(
                new AuditLogEntry
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
                }
            );
        }
        // Do NOT call SaveChanges — entries commit atomically with the entity changes
    }

    [return: NotNullIfNotNull(nameof(value))]
    private static string? _Truncate(string? value, int maxLength)
    {
        return value is { Length: var len } && len > maxLength ? value[..maxLength] : value;
    }
}
