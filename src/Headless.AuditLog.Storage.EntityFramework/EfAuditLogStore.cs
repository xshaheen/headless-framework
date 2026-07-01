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

    // Guards mutations of _entriesByContext and the inner HashSet values. EF Core itself rejects
    // truly concurrent DbContext access — this lock is defensive depth against the misuse path
    // where PrepareForRetry / _ReleaseEntry / the _AddEntries catch block race each other for the
    // same context's tracked-entries entry. Cheap on the single-threaded happy path; cheap insurance
    // on the wrong-but-survivable path.
    private readonly Lock _gate = new();

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

        return Task.FromResult<IReadOnlyList<IAuditLogStoreEntry>>(_AddEntries(entries, _AsDbContext(savingContext)));
    }

    /// <inheritdoc />
    public void PrepareForRetry(object savingContext)
    {
        var context = _AsDbContext(savingContext);

        lock (_gate)
        {
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
    }

    private static DbContext _AsDbContext(object savingContext)
    {
        // Enforce the runtime contract: callers must supply the EF Core context executing SaveChanges.
        // Without this, a wrong-typed context would silently route audit entries to nowhere.
        Argument.IsAssignableToType<DbContext>(savingContext);
        return (DbContext)savingContext;
    }

    private List<IAuditLogStoreEntry> _AddEntries(IReadOnlyList<AuditLogEntryData> entries, DbContext context)
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
                    UserId = AuditLogFieldLimits.Truncate(entry.UserId, AuditLogFieldLimits.UserId),
                    AccountId = AuditLogFieldLimits.Truncate(entry.AccountId, AuditLogFieldLimits.AccountId),
                    TenantId = AuditLogFieldLimits.Truncate(entry.TenantId, AuditLogFieldLimits.TenantId),
                    IpAddress = AuditLogFieldLimits.Truncate(entry.IpAddress, AuditLogFieldLimits.IpAddress),
                    UserAgent = AuditLogFieldLimits.Truncate(entry.UserAgent, AuditLogFieldLimits.UserAgent),
                    CorrelationId = AuditLogFieldLimits.Truncate(
                        entry.CorrelationId,
                        AuditLogFieldLimits.CorrelationId
                    ),
                    Action = AuditLogFieldLimits.Truncate(entry.Action, AuditLogFieldLimits.Action),
                    ChangeType = entry.ChangeType,
                    EntityType = AuditLogFieldLimits.Truncate(entry.EntityType, AuditLogFieldLimits.EntityType),
                    EntityId = AuditLogFieldLimits.Truncate(entry.EntityId, AuditLogFieldLimits.EntityId),
                    OldValues = entry.OldValues,
                    NewValues = entry.NewValues,
                    ChangedFields = entry.ChangedFields,
                    Success = entry.Success,
                    ErrorCode = AuditLogFieldLimits.Truncate(entry.ErrorCode, AuditLogFieldLimits.ErrorCode),
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
                lock (_gate)
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
            }

            throw;
        }
    }

    private void _TrackEntry(DbContext context, AuditLogEntry entry)
    {
        lock (_gate)
        {
            if (!_entriesByContext.TryGetValue(context, out var entries))
            {
                entries = new HashSet<AuditLogEntry>(ReferenceEqualityComparer.Instance);
                _entriesByContext.Add(context, entries);
            }

            entries.Add(entry);
        }
    }

    private void _ReleaseEntry(DbContext context, AuditLogEntry entry)
    {
        lock (_gate)
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
