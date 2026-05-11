// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.AuditLog;
using Headless.EntityFramework.ChangeTrackers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Headless.EntityFramework.Contexts;

/// <summary>
/// Shared save pipeline used by both <see cref="HeadlessDbContext"/> and
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
internal sealed class HeadlessDbContextRuntime
{
    private readonly HeadlessEntityFrameworkNavigationModifiedTracker _navigationModifiedTracker = new();
    private readonly DbContext _db;
    private readonly IHeadlessEntityModelProcessor _entityProcessor;

    public string? TenantId => _entityProcessor.TenantId;

    private ILogger? AuditLogger => field ??= _db.GetServiceOrDefault<ILoggerFactory>()?.CreateLogger(_db.GetType());

    /// <summary>
    /// Shared save pipeline used by both <see cref="HeadlessDbContext"/> and
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
    public HeadlessDbContextRuntime(DbContext db, IHeadlessEntityModelProcessor entityProcessor)
    {
        _db = db;
        _entityProcessor = entityProcessor;
        _SyncNavigationTracker();
    }

    public async Task<int> SaveChangesAsync(
        Func<List<EmitterLocalMessages>, IDbContextTransaction, CancellationToken, Task> publishLocalAsync,
        Func<List<EmitterDistributedMessages>, IDbContextTransaction, CancellationToken, Task> publishDistributedAsync,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    )
    {
        var report = _entityProcessor.ProcessEntries(_db);
        var auditEntries = HeadlessAuditPersistence.CaptureEntries(_db, AuditLogger);

        var state = new AsyncSaveState(
            _db,
            report,
            auditEntries,
            acceptAllChangesOnSuccess,
            publishLocalAsync,
            publishDistributedAsync,
            baseSaveChangesAsync,
            _navigationModifiedTracker,
            cancellationToken
        );

        if (_db.Database.CurrentTransaction is not null)
        {
            return await _ExecuteWithinCurrentTransactionAsync(state).ConfigureAwait(false);
        }

        if (!_RequiresExplicitTransaction(auditEntries, report))
        {
            var result = await baseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            _CompleteSuccessfulSave(report, _navigationModifiedTracker, _db, default, acceptAllChangesOnSuccess);
            return result;
        }

        return await _db
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(state, _ExecuteWithNewTransactionAsync)
            .ConfigureAwait(false);
    }

    public int SaveChanges(
        Action<List<EmitterLocalMessages>, IDbContextTransaction> publishLocal,
        Action<List<EmitterDistributedMessages>, IDbContextTransaction> publishDistributed,
        Func<bool, int> baseSaveChanges,
        bool acceptAllChangesOnSuccess
    )
    {
#pragma warning disable MA0045 // Sync SaveChanges intentionally wraps EF sync APIs.
        var report = _entityProcessor.ProcessEntries(_db);
        var auditEntries = HeadlessAuditPersistence.CaptureEntries(_db, AuditLogger);

        var state = new SaveState(
            _db,
            report,
            auditEntries,
            acceptAllChangesOnSuccess,
            publishLocal,
            publishDistributed,
            baseSaveChanges,
            _navigationModifiedTracker
        );

        if (_db.Database.CurrentTransaction is not null)
        {
            return _ExecuteWithinCurrentTransaction(state);
        }

        if (!_RequiresExplicitTransaction(auditEntries, report))
        {
            var result = baseSaveChanges(acceptAllChangesOnSuccess);
            _CompleteSuccessfulSave(report, _navigationModifiedTracker, _db, default, acceptAllChangesOnSuccess);
            return result;
        }

        return _db.Database.CreateExecutionStrategy().Execute(state, _ExecuteWithNewTransaction);
#pragma warning restore MA0045
    }

    public static void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddBuildingBlocksPrimitivesConvertersMappings();
    }

    public static void ConfigureDefaultSchema(ModelBuilder modelBuilder, string defaultSchema)
    {
        if (!defaultSchema.IsNullOrWhiteSpace())
        {
            modelBuilder.HasDefaultSchema(defaultSchema);
        }
    }

    public void ProcessModelCreating(ModelBuilder modelBuilder)
    {
        _entityProcessor.ProcessModelCreating(modelBuilder);
    }

    private static Task<int> _ExecuteWithinCurrentTransactionAsync(AsyncSaveState state)
    {
        var currentTransaction = state.Context.Database.CurrentTransaction!;
        return _SaveWithinTransactionAsync(state, currentTransaction, commitTransaction: false);
    }

    private static int _ExecuteWithinCurrentTransaction(SaveState state)
    {
        var currentTransaction = state.Context.Database.CurrentTransaction!;
        return _SaveWithinTransaction(state, currentTransaction, commitTransaction: false);
    }

    private static async Task<int> _ExecuteWithNewTransactionAsync(AsyncSaveState state)
    {
        await using var transaction = await state
            .Context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, state.CancellationToken)
            .ConfigureAwait(false);

        return await _SaveWithinTransactionAsync(state, transaction, commitTransaction: true).ConfigureAwait(false);
    }

    private static int _ExecuteWithNewTransaction(SaveState state)
    {
#pragma warning disable MA0045 // Sync intentionally
        using var transaction = state.Context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
        return _SaveWithinTransaction(state, transaction, commitTransaction: true);
#pragma warning restore MA0045
    }

    private static async Task<int> _SaveWithinTransactionAsync(
        AsyncSaveState state,
        IDbContextTransaction transaction,
        bool commitTransaction
    )
    {
        // Owning retry-prep here means the new-transaction path cannot forget to detach
        // stale audit entries left over from a prior execution-strategy attempt.
        if (commitTransaction)
        {
            HeadlessAuditPersistence.PrepareForRetry(state.Context);
        }

        HeadlessAuditSaveResult auditSave = default;

        try
        {
            if (state.Report.LocalEmitters.Count > 0)
            {
                await state
                    .PublishLocalAsync(state.Report.LocalEmitters, transaction, state.CancellationToken)
                    .ConfigureAwait(false);
            }

            var deferAcceptAllChanges = HeadlessAuditPersistence.HasEntries(state.AuditEntries);
            var result = await state
                .BaseSaveChangesAsync(
                    !deferAcceptAllChanges && state.AcceptAllChangesOnSuccess,
                    state.CancellationToken
                )
                .ConfigureAwait(false);

            auditSave = await HeadlessAuditPersistence
                .ResolveAndPersistAsync(
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

            if (commitTransaction)
            {
                await transaction.CommitAsync(state.CancellationToken).ConfigureAwait(false);
            }

            _CompleteSuccessfulSave(
                state.Report,
                state.NavigationTracker,
                state.Context,
                auditSave,
                state.AcceptAllChangesOnSuccess
            );

            return result;
        }
        catch
        {
            HeadlessAuditPersistence.DetachEntries(auditSave);
            throw;
        }
    }

    private static int _SaveWithinTransaction(
        SaveState state,
        IDbContextTransaction transaction,
        bool commitTransaction
    )
    {
#pragma warning disable MA0045 // Sync intentionally — extracted from the sync SaveChanges path.
        // Owning retry-prep here means the new-transaction path cannot forget to detach
        // stale audit entries left over from a prior execution-strategy attempt.
        if (commitTransaction)
        {
            HeadlessAuditPersistence.PrepareForRetry(state.Context);
        }

        HeadlessAuditSaveResult auditSave = default;

        try
        {
            if (state.Report.LocalEmitters.Count > 0)
            {
                state.PublishLocal(state.Report.LocalEmitters, transaction);
            }

            var deferAcceptAllChanges = HeadlessAuditPersistence.HasEntries(state.AuditEntries);
            var result = state.BaseSaveChanges(!deferAcceptAllChanges && state.AcceptAllChangesOnSuccess);
            auditSave = HeadlessAuditPersistence.ResolveAndPersist(
                state.Context,
                state.AuditEntries,
                state.BaseSaveChanges
            );

            if (state.Report.DistributedEmitters.Count > 0)
            {
                state.PublishDistributed(state.Report.DistributedEmitters, transaction);
            }

            if (commitTransaction)
            {
                transaction.Commit();
            }

            _CompleteSuccessfulSave(
                state.Report,
                state.NavigationTracker,
                state.Context,
                auditSave,
                state.AcceptAllChangesOnSuccess
            );

            return result;
        }
        catch
        {
            HeadlessAuditPersistence.DetachEntries(auditSave);
            throw;
        }
#pragma warning restore MA0045
    }

    private static bool _RequiresExplicitTransaction(
        IReadOnlyList<AuditLogEntryData>? auditEntries,
        ProcessBeforeSaveReport report
    ) =>
        HeadlessAuditPersistence.HasEntries(auditEntries)
        || report.DistributedEmitters.Count > 0
        || report.LocalEmitters.Count > 0;

    private static void _CompleteSuccessfulSave(
        ProcessBeforeSaveReport report,
        HeadlessEntityFrameworkNavigationModifiedTracker navigationTracker,
        DbContext context,
        HeadlessAuditSaveResult auditSave,
        bool acceptAllChangesOnSuccess
    )
    {
        HeadlessAuditPersistence.CompleteSuccessfulSave(context, auditSave, acceptAllChangesOnSuccess);
        navigationTracker.RemoveModifiedEntityEntries();
        report.ClearEmitterMessages();
    }

    private void _SyncNavigationTracker()
    {
        _db.ChangeTracker.Tracked += _navigationModifiedTracker.ChangeTrackerTracked;
        _db.ChangeTracker.StateChanged += _navigationModifiedTracker.ChangeTrackerStateChanged;
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
