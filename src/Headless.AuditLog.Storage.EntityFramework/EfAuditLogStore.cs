// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog;

internal sealed class EfAuditLogStore : IAuditLogStore
{
    // Cache the empty-result task so the empty-entries fast path allocates nothing.
    private static readonly Task<IReadOnlyList<IAuditLogStoreEntry>> _EmptyResultTask = Task.FromResult<
        IReadOnlyList<IAuditLogStoreEntry>
    >([]);

    private readonly Dictionary<DbContext, HashSet<AuditLogEntry>> _entriesByContext = new(
        ReferenceEqualityComparer.Instance
    );

    /// <inheritdoc />
    public IReadOnlyList<IAuditLogStoreEntry> Save(IReadOnlyList<AuditLogEntryData> entries, object savingContext)
    {
        return _AddEntries(entries, _AsDbContext(savingContext));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IAuditLogStoreEntry>> SaveAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        object savingContext,
        CancellationToken cancellationToken = default
    )
    {
        if (entries.Count == 0)
        {
            return _EmptyResultTask;
        }

        return Task.FromResult(_AddEntries(entries, _AsDbContext(savingContext)));
    }

    /// <inheritdoc />
    public void PrepareForRetry(object savingContext)
    {
        var context = _AsDbContext(savingContext);

        if (!_entriesByContext.TryGetValue(context, out var entries))
        {
            return;
        }

        // Snapshot before mutating: HashSet enumeration must not be modified during iteration.
        foreach (var auditEntity in entries.ToArray())
        {
            var entry = context.Entry(auditEntity);

            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }

            if (entry.State != EntityState.Added)
            {
                entries.Remove(auditEntity);
            }
        }

        if (entries.Count == 0)
        {
            _entriesByContext.Remove(context);
        }
    }

    private static DbContext _AsDbContext(object savingContext)
    {
        // Enforce the runtime contract: callers must supply the EF Core context executing SaveChanges.
        // Without this, a wrong-typed context would silently route audit entries to nowhere.
        Argument.IsAssignableToType<DbContext>(savingContext);
        return (DbContext)savingContext;
    }

    private IReadOnlyList<IAuditLogStoreEntry> _AddEntries(IReadOnlyList<AuditLogEntryData> entries, DbContext context)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var set = context.Set<AuditLogEntry>();
        var auditEntries = new List<IAuditLogStoreEntry>(entries.Count);
        // Track in-flight additions so we can roll back partial work if Add or tracking throws.
        List<AuditLogEntry>? addedThisCall = null;

        try
        {
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
                (addedThisCall ??= []).Add(auditEntity);
                _TrackEntry(context, auditEntity);
                auditEntries.Add(new EfAuditLogStoreEntry(this, context, auditEntity));
            }
            // Do NOT call SaveChanges — entries commit atomically with the entity changes
            return auditEntries;
        }
        catch
        {
            // Detach partial additions and remove them from the tracking set so the change tracker
            // does not surface half-applied audit rows on retry or surface-level inspection.
            if (addedThisCall is not null)
            {
                _entriesByContext.TryGetValue(context, out var trackedEntries);

                foreach (var added in addedThisCall)
                {
                    var entry = context.Entry(added);

                    if (entry.State != EntityState.Detached)
                    {
                        entry.State = EntityState.Detached;
                    }

                    trackedEntries?.Remove(added);
                }

                if (trackedEntries is { Count: 0 })
                {
                    _entriesByContext.Remove(context);
                }
            }

            throw;
        }
    }

    private void _TrackEntry(DbContext context, AuditLogEntry entry)
    {
        if (!_entriesByContext.TryGetValue(context, out var entries))
        {
            entries = new HashSet<AuditLogEntry>(ReferenceEqualityComparer.Instance);
            _entriesByContext.Add(context, entries);
        }

        entries.Add(entry);
    }

    private void _ReleaseEntry(DbContext context, AuditLogEntry entry)
    {
        if (!_entriesByContext.TryGetValue(context, out var entries))
        {
            return;
        }

        entries.Remove(entry);

        if (entries.Count == 0)
        {
            _entriesByContext.Remove(context);
        }
    }

    [return: NotNullIfNotNull(nameof(value))]
    private static string? _Truncate(string? value, int maxLength)
    {
        return value is { Length: var len } && len > maxLength ? value[..maxLength] : value;
    }

    private sealed class EfAuditLogStoreEntry(EfAuditLogStore owner, DbContext context, AuditLogEntry entity)
        : IAuditLogStoreEntry
    {
        public void DiscardPendingChanges()
        {
            _DetachFromContext();
        }

        public void ReleaseAfterCommit()
        {
            _DetachFromContext();
        }

        private void _DetachFromContext()
        {
            var entry = context.Entry(entity);

            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }

            owner._ReleaseEntry(context, entity);
        }
    }
}
