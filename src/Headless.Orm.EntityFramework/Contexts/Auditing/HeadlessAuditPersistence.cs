// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

internal sealed class HeadlessAuditPersistence(IServiceProvider serviceProvider, ILogger? logger)
{
    private readonly IAuditChangeCapture? _auditCapture = serviceProvider.GetService<IAuditChangeCapture>();
    private readonly IAuditLogStore? _auditStore = serviceProvider.GetService<IAuditLogStore>();
    private readonly ICurrentUser? _currentUser = serviceProvider.GetService<ICurrentUser>();
    private readonly ICurrentTenant? _currentTenant = serviceProvider.GetService<ICurrentTenant>();
    private readonly ICorrelationIdProvider? _correlationIdProvider =
        serviceProvider.GetService<ICorrelationIdProvider>();
    private readonly IClock? _clock = serviceProvider.GetService<IClock>();
    private readonly ILogger? _logger = logger;

    /// <summary>
    /// Captures audit entries from the change tracker before SaveChanges.
    /// Returns <see langword="null"/> when audit capture is not registered or fails.
    /// </summary>
    public IReadOnlyList<AuditLogEntryData>? CaptureEntries(DbContext context)
    {
        if (_auditCapture is null)
        {
            return null;
        }

        var timestamp = _clock?.UtcNow ?? DateTimeOffset.UtcNow;

        try
        {
            return _auditCapture.CaptureChanges(
                context.ChangeTracker.Entries(),
                _currentUser?.UserId?.ToString(),
                _currentUser?.AccountId?.ToString(),
                _currentTenant?.Id,
                _correlationIdProvider?.CorrelationId,
                timestamp
            );
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Audit change capture failed. Continuing with entity save.");
            return null;
        }
    }

    /// <summary>
    /// Detaches stale audit entries from a prior failed attempt before an execution strategy retry.
    /// </summary>
    public void PrepareForRetry(DbContext context)
    {
        _auditStore?.PrepareForRetry(context);
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
                DetachEntries(new(RequiresManualAcceptAllChanges: true, auditEntries));
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

#pragma warning disable MA0045 // Do not use blocking calls in a sync method (need to make calling method async)
        var auditEntries = _SaveEntries(context, entries);
#pragma warning restore MA0045

        if (auditEntries.Count > 0)
        {
            _SuppressEntries(context, snapshots);

            try
            {
                baseSaveChanges(false);
            }
            catch
            {
                DetachEntries(new(RequiresManualAcceptAllChanges: true, auditEntries));
                throw;
            }
            finally
            {
                _RestoreEntries(context, snapshots);
            }
        }

        return new(RequiresManualAcceptAllChanges: true, auditEntries);
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

        DetachEntries(auditSave);
    }

    public static void DetachEntries(HeadlessAuditSaveResult auditSave)
    {
        if (auditSave.AuditEntries is null)
        {
            return;
        }

        foreach (var entry in auditSave.AuditEntries)
        {
            entry.Detach();
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
#pragma warning disable MA0045 // Do not use blocking calls in a sync method (need to make calling method async)
        // Defensive null-coalesce: contract says non-null but a buggy third-party implementer
        // could return null, which would NRE during the auditEntries.Count guard below.
        return _auditStore?.Save(entries, context) ?? [];
#pragma warning restore MA0045
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

        // Defensive null-coalesce: contract says non-null but a buggy third-party implementer could return null mid-transaction.
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        return await _auditStore.SaveAsync(entries, context, cancellationToken).ConfigureAwait(false) ?? [];
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
                    properties.Add(new(property.Metadata.Name, property.OriginalValue, property.IsModified));
                }

                (snapshots ??= []).Add(new(entry.Entity, entry.State, properties));
            }

            return snapshots ?? (IReadOnlyList<TrackedEntrySnapshot>)[];
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
