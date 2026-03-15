// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        => _AddEntries(entries, savingContext as DbContext ?? dbContext);

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
                entry.State = EntityState.Detached;
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
                    CreatedAt = entry.CreatedAt,
                    UserId = Truncate(entry.UserId, 128),
                    AccountId = Truncate(entry.AccountId, 128),
                    TenantId = Truncate(entry.TenantId, 128),
                    IpAddress = Truncate(entry.IpAddress, 45),
                    UserAgent = Truncate(entry.UserAgent, 512),
                    CorrelationId = Truncate(entry.CorrelationId, 128),
                    Action = Truncate(entry.Action, 256)!,
                    ChangeType = entry.ChangeType,
                    EntityType = Truncate(entry.EntityType, 512),
                    EntityId = Truncate(entry.EntityId, 256),
                    OldValues = entry.OldValues,
                    NewValues = entry.NewValues,
                    ChangedFields = entry.ChangedFields,
                    Success = entry.Success,
                    ErrorCode = Truncate(entry.ErrorCode, 256),
                }
            );
        }
        // Do NOT call SaveChanges — entries commit atomically with the entity changes
    }

    private static string? Truncate(string? value, int maxLength)
        => value is { Length: var len } && len > maxLength ? value[..maxLength] : value;
}
