// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Context;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework;

internal sealed class HeadlessAuditPersistence(
    IServiceProvider serviceProvider,
    ILogger<HeadlessAuditPersistence>? logger = null
) : IHeadlessAuditPersistence
{
    private readonly IAuditChangeCapture? _auditCapture = serviceProvider.GetService<IAuditChangeCapture>();
    private readonly IAuditLogStore? _auditStore = serviceProvider.GetService<IAuditLogStore>();
    private readonly ICurrentUser? _currentUser = serviceProvider.GetService<ICurrentUser>();
    private readonly ICurrentTenant? _currentTenant = serviceProvider.GetService<ICurrentTenant>();

    private readonly ICorrelationIdProvider? _correlationIdProvider =
        serviceProvider.GetService<ICorrelationIdProvider>();

    private readonly TimeProvider _timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
    private readonly IOptions<AuditLogOptions>? _auditOptions = serviceProvider.GetService<IOptions<AuditLogOptions>>();

    /// <summary>
    /// Captures audit entries from a pre-materialized change-tracker snapshot.
    /// Returns <see langword="null"/> when audit capture is not registered or fails (with
    /// <see cref="CaptureErrorStrategy.Continue"/>). Rethrows when configured for
    /// <see cref="CaptureErrorStrategy.Throw"/>.
    /// </summary>
    public IReadOnlyList<AuditLogEntryData>? CaptureEntries(
        IReadOnlyList<EntityEntry> entries,
        HeadlessSaveEntryContext saveContext
    )
    {
        if (_auditCapture is null)
        {
            return null;
        }

        // A save that changes nothing has nothing to capture, so it reads no actor.
        if (!_HasChanges(entries))
        {
            return [];
        }

        var timestamp = _timeProvider.GetUtcNow();

        // The actor is read once per save and shared with the audit-field stampers.
        var (userId, accountId) = _currentUser is null ? default : saveContext.GetActor(_currentUser);

        try
        {
            return _auditCapture.CaptureChanges(
                entries,
                userId?.ToString(),
                accountId?.ToString(),
                _currentTenant?.Id,
                _correlationIdProvider?.CorrelationId,
                timestamp
            );
        }
        catch (Exception ex)
        {
            // Elevated to Error: capture failure means an audit-tracked entity change is going to be
            // persisted without its audit row. Operators must see this in logs.
            logger?.LogAuditCaptureFailed(ex);

            var strategy = _auditOptions?.Value.CaptureErrorStrategy ?? CaptureErrorStrategy.Continue;

            if (strategy == CaptureErrorStrategy.Throw)
            {
                throw;
            }

            return null;
        }
    }

    private static bool _HasChanges(IReadOnlyList<EntityEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cleans up stale audit entries from a prior failed attempt before an execution strategy retry.
    /// </summary>
    public void PrepareForRetry(DbContext context)
    {
        _auditStore?.PrepareForRetry(context);
    }

    public async Task<HeadlessAuditSplit> AddReadyEntriesAsync(
        DbContext context,
        IReadOnlyList<AuditLogEntryData>? entries,
        CancellationToken cancellationToken
    )
    {
        var (ready, deferred) = _Partition(entries);

        if (ready is null)
        {
            return new(default, entries);
        }

        var handles = await _SaveEntriesAsync(context, ready, cancellationToken).ConfigureAwait(false);

        return new(new(RequiresManualAcceptAllChanges: true, handles), deferred);
    }

    public HeadlessAuditSplit AddReadyEntries(DbContext context, IReadOnlyList<AuditLogEntryData>? entries)
    {
        var (ready, deferred) = _Partition(entries);

        if (ready is null)
        {
            return new(default, entries);
        }

        return new(new(RequiresManualAcceptAllChanges: true, _SaveEntries(context, ready)), deferred);
    }

    // Only a store that adds rows to the saving context can run before the entity save; one that writes on its own
    // connection would leave an audit row behind for an entity change that then fails.
    private (List<AuditLogEntryData>? Ready, List<AuditLogEntryData>? Deferred) _Partition(
        IReadOnlyList<AuditLogEntryData>? entries
    )
    {
        if (
            entries is not { Count: > 0 }
            || _auditStore is not { AddsToSavingContext: true }
            || _auditCapture is not IAuditEntityIdResolver resolver
        )
        {
            return (null, null);
        }

        List<AuditLogEntryData>? ready = null;
        List<AuditLogEntryData>? deferred = null;

        foreach (var entry in entries)
        {
            if (resolver.RequiresDeferredResolution(entry))
            {
                (deferred ??= []).Add(entry);
            }
            else
            {
                (ready ??= []).Add(entry);
            }
        }

        return (ready, deferred);
    }

    public async Task<HeadlessAuditSaveResult> ResolveAndPersistAsync(
        DbContext context,
        IReadOnlyList<AuditLogEntryData>? entries,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        CancellationToken cancellationToken
    )
    {
        if (entries is not { Count: > 0 })
        {
            return default;
        }

        _ResolveEntityIds(entries);
        var snapshots = TrackedEntrySnapshot.Capture(context);

        var auditEntries = await _SaveEntriesAsync(context, entries, cancellationToken).ConfigureAwait(false);

        if (auditEntries.Count > 0)
        {
            _SuppressEntries(context, snapshots);

            try
            {
                await baseSaveChangesAsync(false, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                DiscardEntries(new(RequiresManualAcceptAllChanges: true, auditEntries));
                throw;
            }
            finally
            {
                _RestoreEntries(context, snapshots);
            }
        }

        return new(RequiresManualAcceptAllChanges: true, auditEntries);
    }

    public HeadlessAuditSaveResult ResolveAndPersist(
        DbContext context,
        IReadOnlyList<AuditLogEntryData>? entries,
        Func<bool, int> baseSaveChanges
    )
    {
        if (entries is not { Count: > 0 })
        {
            return default;
        }

        _ResolveEntityIds(entries);
        var snapshots = TrackedEntrySnapshot.Capture(context);

        var auditEntries = _SaveEntries(context, entries);

        if (auditEntries.Count > 0)
        {
            _SuppressEntries(context, snapshots);

            try
            {
                baseSaveChanges(false);
            }
            catch
            {
                DiscardEntries(new(RequiresManualAcceptAllChanges: true, auditEntries));
                throw;
            }
            finally
            {
                _RestoreEntries(context, snapshots);
            }
        }

        return new(RequiresManualAcceptAllChanges: true, auditEntries);
    }

    public void CompleteSuccessfulSave(
        DbContext context,
        HeadlessAuditSaveResult auditSave,
        bool acceptAllChangesOnSuccess
    )
    {
        if (!auditSave.RequiresManualAcceptAllChanges)
        {
            return;
        }

        // Detach the audit rows before accepting: AcceptAllChanges would otherwise snapshot every JSON column of
        // each row just before the row leaves the tracker.
        ReleaseEntries(auditSave);

        if (acceptAllChangesOnSuccess)
        {
            context.ChangeTracker.AcceptAllChanges();
        }
    }

    public void DiscardEntries(HeadlessAuditSaveResult auditSave)
    {
        if (auditSave.AuditEntries is null)
        {
            return;
        }

        foreach (var entry in auditSave.AuditEntries)
        {
            entry.DiscardPendingChanges();
        }
    }

    public void ReleaseEntries(HeadlessAuditSaveResult auditSave)
    {
        if (auditSave.AuditEntries is null)
        {
            return;
        }

        foreach (var entry in auditSave.AuditEntries)
        {
            entry.ReleaseAfterCommit();
        }
    }

    private void _ResolveEntityIds(IReadOnlyList<AuditLogEntryData> entries)
    {
        if (_auditCapture is IAuditEntityIdResolver resolver)
        {
            resolver.ResolveEntityIds(entries);
        }
    }

    private IReadOnlyList<IAuditLogStoreEntry> _SaveEntries(DbContext context, IReadOnlyList<AuditLogEntryData> entries)
    {
        if (_auditStore is null)
        {
            return [];
        }

        return _auditStore.Save(entries, context)
            ?? throw new InvalidOperationException(
                "IAuditLogStore.Save returned null; implementation must return an empty list when no entries are saved, never null."
            );
    }

    private async Task<IReadOnlyList<IAuditLogStoreEntry>> _SaveEntriesAsync(
        DbContext context,
        IReadOnlyList<AuditLogEntryData> entries,
        CancellationToken cancellationToken
    )
    {
        if (_auditStore is null)
        {
            return [];
        }

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        var result = await _auditStore.SaveAsync(entries, context, cancellationToken).ConfigureAwait(false);

        return result
            ?? throw new InvalidOperationException(
                "IAuditLogStore.SaveAsync returned null; implementation must return an empty list when no entries are saved, never null."
            );
    }

    private static void _SuppressEntries(DbContext context, IReadOnlyList<TrackedEntrySnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var entry = context.Entry(snapshot.Entity);
            entry.State = snapshot.State == EntityState.Deleted ? EntityState.Detached : EntityState.Unchanged;
        }
    }

    private static void _RestoreEntries(DbContext context, IReadOnlyList<TrackedEntrySnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            snapshot.Restore(context);
        }
    }

    private sealed record TrackedEntrySnapshot(
        object Entity,
        EntityState State,
        IReadOnlyList<PropertySnapshot> Properties
    )
    {
        public static List<TrackedEntrySnapshot> Capture(DbContext context)
        {
            List<TrackedEntrySnapshot>? snapshots = null;

            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                {
                    continue;
                }

                var properties = new List<PropertySnapshot>();

                foreach (var property in entry.Properties)
                {
                    properties.Add(
                        new(property.Metadata.Name, property.OriginalValue, property.CurrentValue, property.IsModified)
                    );
                }

                (snapshots ??= []).Add(new(entry.Entity, entry.State, properties));
            }

            return snapshots ?? [];
        }

        public void Restore(DbContext context)
        {
            var entry = context.Entry(Entity);
            entry.State = EntityState.Unchanged;

            foreach (var property in Properties)
            {
                var propertyEntry = entry.Property(property.Name);

                // The Modified→Unchanged transition above rejected the pending changes, so EF reset
                // each current value to its original — including store-generated values the business
                // save read back (RETURNING). Put the saved current values back first, then the
                // original and the modified flag, so change detection ends consistent. The equality
                // guard leaves key and untouched properties out of EF's key-modification checks.
                if (!Equals(propertyEntry.CurrentValue, property.CurrentValue))
                {
                    propertyEntry.CurrentValue = property.CurrentValue;
                }

                propertyEntry.OriginalValue = property.OriginalValue;
                propertyEntry.IsModified = property.IsModified;
            }

            if (State != EntityState.Modified)
            {
                entry.State = State;
            }
        }
    }

    private sealed record PropertySnapshot(string Name, object? OriginalValue, object? CurrentValue, bool IsModified);
}

internal static partial class HeadlessAuditPersistenceLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "AuditCaptureFailed",
        Level = LogLevel.Error,
        Message = "Audit change capture failed."
    )]
    public static partial void LogAuditCaptureFailed(this ILogger logger, Exception exception);
}
