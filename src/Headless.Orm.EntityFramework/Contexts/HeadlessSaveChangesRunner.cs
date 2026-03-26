// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Abstractions;
using Headless.AuditLog;
using Headless.Orm.EntityFramework.ChangeTrackers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Headless.Orm.EntityFramework.Contexts;

/// <summary>
/// Shared save pipeline runner used by both <c>HeadlessDbContext</c> and
/// <c>HeadlessIdentityDbContext</c>. Centralizes entity processing, audit capture,
/// message publishing, transaction management, and execution strategy retry logic.
/// </summary>
/// <remarks>
/// <para>
/// <b>Transaction behavior:</b> An explicit <c>ReadCommitted</c> transaction is started
/// whenever audit entries are captured OR message emitters are present. This ensures audit
/// entries are committed atomically with entity changes across all context types.
/// Prior to this consolidation, <c>HeadlessIdentityDbContext</c> persisted audit entries
/// outside an explicit transaction — the unified behavior is intentionally stricter.
/// </para>
/// </remarks>
internal static class HeadlessSaveChangesRunner
{
    public static async Task<int> ExecuteAsync(
        DbContext context,
        IHeadlessEntityModelProcessor entityProcessor,
        HeadlessEntityFrameworkNavigationModifiedTracker navigationTracker,
        Func<List<EmitterLocalMessages>, IDbContextTransaction, CancellationToken, Task> publishLocalAsync,
        Func<List<EmitterDistributedMessages>, IDbContextTransaction, CancellationToken, Task> publishDistributedAsync,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        ILogger? auditLogger,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    )
    {
        var report = entityProcessor.ProcessEntries(context);
        var auditEntries = _CaptureAuditEntries(context, auditLogger);

        var state = new AsyncSaveState(
            context,
            report,
            auditEntries,
            acceptAllChangesOnSuccess,
            publishLocalAsync,
            publishDistributedAsync,
            baseSaveChangesAsync,
            navigationTracker,
            cancellationToken
        );

        if (context.Database.CurrentTransaction is not null)
        {
            return await _ExecuteWithinCurrentTransactionAsync(state).ConfigureAwait(false);
        }

        if (!_RequiresExplicitTransaction(auditEntries, report))
        {
            var result = await baseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            _CompleteSuccessfulSave(report, navigationTracker);
            return result;
        }

        return await context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(state, _ExecuteWithNewTransactionAsync)
            .ConfigureAwait(false);
    }

    public static int Execute(
        DbContext context,
        IHeadlessEntityModelProcessor entityProcessor,
        HeadlessEntityFrameworkNavigationModifiedTracker navigationTracker,
        Action<List<EmitterLocalMessages>, IDbContextTransaction> publishLocal,
        Action<List<EmitterDistributedMessages>, IDbContextTransaction> publishDistributed,
        Func<bool, int> baseSaveChanges,
        ILogger? auditLogger,
        bool acceptAllChangesOnSuccess
    )
    {
#pragma warning disable MA0045 // Sync SaveChanges intentionally wraps EF sync APIs.
        var report = entityProcessor.ProcessEntries(context);
        var auditEntries = _CaptureAuditEntries(context, auditLogger);

        var state = new SaveState(
            context,
            report,
            auditEntries,
            acceptAllChangesOnSuccess,
            publishLocal,
            publishDistributed,
            baseSaveChanges,
            navigationTracker
        );

        if (context.Database.CurrentTransaction is not null)
        {
            return _ExecuteWithinCurrentTransaction(state);
        }

        if (!_RequiresExplicitTransaction(auditEntries, report))
        {
            var result = baseSaveChanges(acceptAllChangesOnSuccess);
            _CompleteSuccessfulSave(report, navigationTracker);
            return result;
        }

        return context.Database.CreateExecutionStrategy().Execute(state, _ExecuteWithNewTransaction);
#pragma warning restore MA0045
    }

    private static async Task<int> _ExecuteWithinCurrentTransactionAsync(AsyncSaveState state)
    {
        var currentTransaction = state.Context.Database.CurrentTransaction!;

        if (state.Report.LocalEmitters.Count > 0)
        {
            await state
                .PublishLocalAsync(state.Report.LocalEmitters, currentTransaction, state.CancellationToken)
                .ConfigureAwait(false);
        }

        var result = await state
            .BaseSaveChangesAsync(state.AcceptAllChangesOnSuccess, state.CancellationToken)
            .ConfigureAwait(false);

        await _ResolveAndPersistAuditAsync(
                state.Context,
                state.AuditEntries,
                state.BaseSaveChangesAsync,
                state.CancellationToken
            )
            .ConfigureAwait(false);

        if (state.Report.DistributedEmitters.Count > 0)
        {
            await state
                .PublishDistributedAsync(state.Report.DistributedEmitters, currentTransaction, state.CancellationToken)
                .ConfigureAwait(false);
        }

        _CompleteSuccessfulSave(state.Report, state.NavigationTracker);

        return result;
    }

    private static int _ExecuteWithinCurrentTransaction(SaveState state)
    {
        var currentTransaction = state.Context.Database.CurrentTransaction!;

        if (state.Report.LocalEmitters.Count > 0)
        {
            state.PublishLocal(state.Report.LocalEmitters, currentTransaction);
        }

        var result = state.BaseSaveChanges(state.AcceptAllChangesOnSuccess);
        _ResolveAndPersistAudit(state.Context, state.AuditEntries, state.BaseSaveChanges);

        if (state.Report.DistributedEmitters.Count > 0)
        {
            state.PublishDistributed(state.Report.DistributedEmitters, currentTransaction);
        }

        _CompleteSuccessfulSave(state.Report, state.NavigationTracker);

        return result;
    }

    private static async Task<int> _ExecuteWithNewTransactionAsync(AsyncSaveState state)
    {
        _PrepareForRetry(state.Context);

        await using var transaction = await state
            .Context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, state.CancellationToken)
            .ConfigureAwait(false);

        if (state.Report.LocalEmitters.Count > 0)
        {
            await state
                .PublishLocalAsync(state.Report.LocalEmitters, transaction, state.CancellationToken)
                .ConfigureAwait(false);
        }

        var result = await state
            .BaseSaveChangesAsync(state.AcceptAllChangesOnSuccess, state.CancellationToken)
            .ConfigureAwait(false);

        await _ResolveAndPersistAuditAsync(
                state.Context,
                state.AuditEntries,
                state.BaseSaveChangesAsync,
                state.CancellationToken
            )
            .ConfigureAwait(false);

        if (state.Report.DistributedEmitters.Count > 0)
        {
            await state
                .PublishDistributedAsync(state.Report.DistributedEmitters, transaction, state.CancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(state.CancellationToken).ConfigureAwait(false);

        _CompleteSuccessfulSave(state.Report, state.NavigationTracker);

        return result;
    }

    private static int _ExecuteWithNewTransaction(SaveState state)
    {
#pragma warning disable MA0045 // Sync intentionally
        _PrepareForRetry(state.Context);

        using var transaction = state.Context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
        if (state.Report.LocalEmitters.Count > 0)
        {
            state.PublishLocal(state.Report.LocalEmitters, transaction);
        }

        var result = state.BaseSaveChanges(state.AcceptAllChangesOnSuccess);
        _ResolveAndPersistAudit(state.Context, state.AuditEntries, state.BaseSaveChanges);

        if (state.Report.DistributedEmitters.Count > 0)
        {
            state.PublishDistributed(state.Report.DistributedEmitters, transaction);
        }

        transaction.Commit();
        _CompleteSuccessfulSave(state.Report, state.NavigationTracker);

        return result;
#pragma warning restore MA0045
    }

    #region Audit Helpers

    /// <summary>
    /// Captures audit entries from the change tracker before SaveChanges.
    /// Returns <see langword="null"/> when audit capture is not registered or fails.
    /// </summary>
    private static IReadOnlyList<AuditLogEntryData>? _CaptureAuditEntries(DbContext context, ILogger? logger)
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
    private static void _PrepareForRetry(DbContext context)
    {
        var store = context.GetServiceOrDefault<IAuditLogStore>();
        store?.PrepareForRetry(context);
    }

    private static async Task _ResolveAndPersistAuditAsync(
        DbContext context,
        IReadOnlyList<AuditLogEntryData>? entries,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        CancellationToken cancellationToken
    )
    {
        if (entries is not { Count: > 0 })
        {
            return;
        }

        _ResolveEntityIds(context, entries);
        await _SaveAuditEntriesAsync(context, entries, cancellationToken).ConfigureAwait(false);
        await baseSaveChangesAsync(true, cancellationToken).ConfigureAwait(false);
    }

    private static void _ResolveAndPersistAudit(
        DbContext context,
        IReadOnlyList<AuditLogEntryData>? entries,
        Func<bool, int> baseSaveChanges
    )
    {
        if (entries is not { Count: > 0 })
        {
            return;
        }

        _ResolveEntityIds(context, entries);
        _SaveAuditEntries(context, entries);
        baseSaveChanges(true);
    }

    /// <summary>
    /// Resolves deferred entity IDs on captured audit entries after entity SaveChanges
    /// has assigned store-generated keys.
    /// </summary>
    private static void _ResolveEntityIds(DbContext context, IReadOnlyList<AuditLogEntryData> entries)
    {
        if (context.GetServiceOrDefault<IAuditChangeCapture>() is IAuditEntityIdResolver resolver)
        {
            resolver.ResolveEntityIds(entries);
        }
    }

    /// <summary>
    /// Adds audit entries to the saving context for synchronous persist.
    /// </summary>
    private static void _SaveAuditEntries(DbContext context, IReadOnlyList<AuditLogEntryData> entries)
    {
        var store = context.GetServiceOrDefault<IAuditLogStore>();
        store?.Save(entries, context);
    }

    /// <summary>
    /// Adds audit entries to the saving context for asynchronous persist.
    /// </summary>
    private static async Task _SaveAuditEntriesAsync(
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

    #endregion

    private static bool _RequiresExplicitTransaction(
        IReadOnlyList<AuditLogEntryData>? auditEntries,
        ProcessBeforeSaveReport report
    ) => auditEntries is { Count: > 0 } || report.DistributedEmitters.Count > 0 || report.LocalEmitters.Count > 0;

    private static void _CompleteSuccessfulSave(
        ProcessBeforeSaveReport report,
        HeadlessEntityFrameworkNavigationModifiedTracker navigationTracker
    )
    {
        navigationTracker.RemoveModifiedEntityEntries();
        report.ClearEmitterMessages();
    }

    private readonly record struct AsyncSaveState(
        DbContext Context,
        ProcessBeforeSaveReport Report,
        IReadOnlyList<AuditLogEntryData>? AuditEntries,
        bool AcceptAllChangesOnSuccess,
        Func<List<EmitterLocalMessages>, IDbContextTransaction, CancellationToken, Task> PublishLocalAsync,
        Func<List<EmitterDistributedMessages>, IDbContextTransaction, CancellationToken, Task> PublishDistributedAsync,
        Func<bool, CancellationToken, Task<int>> BaseSaveChangesAsync,
        HeadlessEntityFrameworkNavigationModifiedTracker NavigationTracker,
        CancellationToken CancellationToken
    );

    private readonly record struct SaveState(
        DbContext Context,
        ProcessBeforeSaveReport Report,
        IReadOnlyList<AuditLogEntryData>? AuditEntries,
        bool AcceptAllChangesOnSuccess,
        Action<List<EmitterLocalMessages>, IDbContextTransaction> PublishLocal,
        Action<List<EmitterDistributedMessages>, IDbContextTransaction> PublishDistributed,
        Func<bool, int> BaseSaveChanges,
        HeadlessEntityFrameworkNavigationModifiedTracker NavigationTracker
    );
}
