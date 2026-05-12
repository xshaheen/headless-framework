// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

internal sealed class HeadlessAuditPersistence : IHeadlessAuditPersistence
{
    private readonly IAuditChangeCapture? _auditCapture;
    private readonly IAuditLogStore? _auditStore;
    private readonly ICurrentUser? _currentUser;
    private readonly ICurrentTenant? _currentTenant;
    private readonly ICorrelationIdProvider? _correlationIdProvider;
    private readonly IClock? _clock;
    private readonly IOptions<AuditLogOptions>? _auditOptions;
    private readonly ILogger<HeadlessAuditPersistence>? _logger;

    public HeadlessAuditPersistence(IServiceProvider serviceProvider, ILogger<HeadlessAuditPersistence>? logger = null)
    {
        _auditCapture = serviceProvider.GetService<IAuditChangeCapture>();
        _auditStore = serviceProvider.GetService<IAuditLogStore>();
        _currentUser = serviceProvider.GetService<ICurrentUser>();
        _currentTenant = serviceProvider.GetService<ICurrentTenant>();
        _correlationIdProvider = serviceProvider.GetService<ICorrelationIdProvider>();
        _clock = serviceProvider.GetService<IClock>();
        _auditOptions = serviceProvider.GetService<IOptions<AuditLogOptions>>();
        _logger = logger;
    }

    /// <summary>
    /// Captures audit entries from a pre-materialized change-tracker snapshot.
    /// Returns <see langword="null"/> when audit capture is not registered or fails (with
    /// <see cref="CaptureErrorStrategy.Continue"/>). Rethrows when configured for
    /// <see cref="CaptureErrorStrategy.Throw"/>.
    /// </summary>
    public IReadOnlyList<AuditLogEntryData>? CaptureEntries(IReadOnlyList<EntityEntry> entries)
    {
        if (_auditCapture is null)
        {
            return null;
        }

        var timestamp = _clock?.UtcNow ?? DateTimeOffset.UtcNow;

        try
        {
            return _auditCapture.CaptureChanges(
                entries,
                _currentUser?.UserId?.ToString(),
                _currentUser?.AccountId?.ToString(),
                _currentTenant?.Id,
                _correlationIdProvider?.CorrelationId,
                timestamp
            );
        }
        catch (Exception ex)
        {
            // Elevated to Error: capture failure means an audit-tracked entity change is going to be
            // persisted without its audit row. Operators must see this in logs.
            _logger?.LogError(ex, "Audit change capture failed.");

            var strategy = _auditOptions?.Value.CaptureErrorStrategy ?? CaptureErrorStrategy.Continue;

            if (strategy == CaptureErrorStrategy.Throw)
            {
                throw;
            }

            return null;
        }
    }

    /// <summary>
    /// Cleans up stale audit entries from a prior failed attempt before an execution strategy retry.
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

        if (acceptAllChangesOnSuccess)
        {
            context.ChangeTracker.AcceptAllChanges();
        }

        ReleaseEntries(auditSave);
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

#pragma warning disable MA0045 // Do not use blocking calls in a sync method (need to make calling method async)
        var result = _auditStore.Save(entries, context);
#pragma warning restore MA0045

        if (result is null)
        {
            throw new InvalidOperationException(
                "IAuditLogStore.Save returned null; implementation must return an empty list when no entries are saved, never null."
            );
        }

        return result;
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

        if (result is null)
        {
            throw new InvalidOperationException(
                "IAuditLogStore.SaveAsync returned null; implementation must return an empty list when no entries are saved, never null."
            );
        }

        return result;
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
