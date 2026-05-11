// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;

namespace Headless.Orm.EntityFramework.Contexts;

internal static class HeadlessAuditPersistence
{
    public static bool HasEntries(IReadOnlyList<AuditLogEntryData>? entries) => entries is { Count: > 0 };

    /// <summary>
    /// Captures audit entries from the change tracker before SaveChanges.
    /// Returns <see langword="null"/> when audit capture is not registered or fails.
    /// </summary>
    public static IReadOnlyList<AuditLogEntryData>? CaptureEntries(DbContext context, ILogger? logger)
    {
        var auditCapture = context.GetServiceOrDefault<IAuditChangeCapture>();

        if (auditCapture is null)
        {
            return null;
        }

        var currentUser = context.GetServiceOrDefault<ICurrentUser>();
        var currentTenant = context.GetServiceOrDefault<ICurrentTenant>();
        var correlationIdProvider = context.GetServiceOrDefault<ICorrelationIdProvider>();
        var clock = context.GetServiceOrDefault<IClock>();
        var timestamp = clock?.UtcNow ?? DateTimeOffset.UtcNow;

        try
        {
            return auditCapture.CaptureChanges(
                context.ChangeTracker.Entries(),
                currentUser?.UserId?.ToString(),
                currentUser?.AccountId?.ToString(),
                currentTenant?.Id,
                correlationIdProvider?.CorrelationId,
                timestamp
            );
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Audit change capture failed. Continuing with entity save.");
            return null;
        }
    }

    /// <summary>
    /// Detaches stale audit entries from a prior failed attempt before an execution strategy retry.
    /// </summary>
    public static void PrepareForRetry(DbContext context)
    {
        var store = context.GetServiceOrDefault<IAuditLogStore>();
        store?.PrepareForRetry(context);
    }

    public static async Task<HeadlessAuditSaveResult> ResolveAndPersistAsync(
        DbContext context,
        IReadOnlyList<AuditLogEntryData>? entries,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        CancellationToken cancellationToken
    )
    {
        if (entries is not { Count: > 0 } capturedEntries)
        {
            return default;
        }

        _ResolveEntityIds(context, capturedEntries);
        var originalEntities = _CaptureTrackedEntities(context);
        var snapshots = TrackedEntrySnapshot.Capture(context);

        await _SaveEntriesAsync(context, capturedEntries, cancellationToken).ConfigureAwait(false);
        var auditEntities = _CaptureNewAddedEntities(context, originalEntities);

        if (auditEntities.Count > 0)
        {
            _SuppressEntries(context, snapshots);

            try
            {
                await baseSaveChangesAsync(false, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _RestoreEntries(context, snapshots);
            }
        }

        return new(RequiresManualAcceptAllChanges: true, auditEntities);
    }

    public static HeadlessAuditSaveResult ResolveAndPersist(
        DbContext context,
        IReadOnlyList<AuditLogEntryData>? entries,
        Func<bool, int> baseSaveChanges
    )
    {
        if (entries is not { Count: > 0 } capturedEntries)
        {
            return default;
        }

        _ResolveEntityIds(context, capturedEntries);
        var originalEntities = _CaptureTrackedEntities(context);
        var snapshots = TrackedEntrySnapshot.Capture(context);

        _SaveEntries(context, capturedEntries);
        var auditEntities = _CaptureNewAddedEntities(context, originalEntities);

        if (auditEntities.Count > 0)
        {
            _SuppressEntries(context, snapshots);

            try
            {
                baseSaveChanges(false);
            }
            finally
            {
                _RestoreEntries(context, snapshots);
            }
        }

        return new(RequiresManualAcceptAllChanges: true, auditEntities);
    }

    public static void CompleteSuccessfulSave(
        DbContext context,
        HeadlessAuditSaveResult auditSave,
        bool acceptAllChangesOnSuccess
    )
    {
        if (!auditSave.RequiresManualAcceptAllChanges)
        {
            return;
        }

        if (acceptAllChangesOnSuccess)
        {
            context.ChangeTracker.AcceptAllChanges();
        }
        else
        {
            DetachEntries(context, auditSave);
        }
    }

    public static void DetachEntries(DbContext context, HeadlessAuditSaveResult auditSave)
    {
        if (auditSave.AuditEntities is null)
        {
            return;
        }

        foreach (var entity in auditSave.AuditEntities)
        {
            var entry = context.Entry(entity);

            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static void _ResolveEntityIds(DbContext context, IReadOnlyList<AuditLogEntryData> entries)
    {
        if (context.GetServiceOrDefault<IAuditChangeCapture>() is IAuditEntityIdResolver resolver)
        {
            resolver.ResolveEntityIds(entries);
        }
    }

    private static void _SaveEntries(DbContext context, IReadOnlyList<AuditLogEntryData> entries)
    {
        var store = context.GetServiceOrDefault<IAuditLogStore>();
        store?.Save(entries, context);
    }

    private static async Task _SaveEntriesAsync(
        DbContext context,
        IReadOnlyList<AuditLogEntryData> entries,
        CancellationToken cancellationToken
    )
    {
        var store = context.GetServiceOrDefault<IAuditLogStore>();

        if (store is not null)
        {
            await store.SaveAsync(entries, context, cancellationToken).ConfigureAwait(false);
        }
    }

    private static HashSet<object> _CaptureTrackedEntities(DbContext context)
    {
        var entities = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var entry in context.ChangeTracker.Entries())
        {
            entities.Add(entry.Entity);
        }

        return entities;
    }

    private static IReadOnlyList<object> _CaptureNewAddedEntities(DbContext context, HashSet<object> originalEntities)
    {
        return context
            .ChangeTracker.Entries()
            .Where(entry => entry.State == EntityState.Added && !originalEntities.Contains(entry.Entity))
            .Select(entry => entry.Entity)
            .ToArray();
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
        public static IReadOnlyList<TrackedEntrySnapshot> Capture(DbContext context)
        {
            return context
                .ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => new TrackedEntrySnapshot(
                    entry.Entity,
                    entry.State,
                    entry
                        .Properties.Select(static property => new PropertySnapshot(
                            property.Metadata.Name,
                            property.OriginalValue,
                            property.IsModified
                        ))
                        .ToArray()
                ))
                .ToArray();
        }

        public void Restore(DbContext context)
        {
            var entry = context.Entry(Entity);
            entry.State = EntityState.Unchanged;

            foreach (var property in Properties)
            {
                var propertyEntry = entry.Property(property.Name);
                propertyEntry.OriginalValue = property.OriginalValue;
                propertyEntry.IsModified = property.IsModified;
            }

            if (State != EntityState.Modified)
            {
                entry.State = State;
            }
        }
    }

    private sealed record PropertySnapshot(string Name, object? OriginalValue, bool IsModified);
}

internal readonly record struct HeadlessAuditSaveResult(
    bool RequiresManualAcceptAllChanges,
    IReadOnlyList<object>? AuditEntities
);
