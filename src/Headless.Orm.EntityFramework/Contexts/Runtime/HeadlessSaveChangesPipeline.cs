// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.ExceptionServices;
using Headless.AuditLog;
using Headless.EntityFramework.Messaging;
using Headless.EntityFramework.Processors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.EntityFramework;

/// <summary>
/// Coordinates the per-<c>SaveChanges</c> work of a <see cref="HeadlessDbContext"/>: runs the ordered
/// chain of <see cref="IHeadlessSaveEntryProcessor"/> stages, captures audit entries, dispatches local
/// messages within the active transaction, persists the entity batch, and enqueues distributed messages
/// post-success before committing.
/// </summary>
/// <remarks>
/// Implementations own the transaction boundary. When an explicit transaction is already on the context
/// the pipeline reuses it; otherwise it opens a transaction wrapped by the execution strategy so audit and
/// message-emitter work commit atomically with the entity batch.
/// </remarks>
public interface IHeadlessSaveChangesPipeline
{
    Task<int> SaveChangesAsync(
        DbContext context,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    );

    int SaveChanges(DbContext context, Func<bool, int> baseSaveChanges, bool acceptAllChangesOnSuccess);
}

/// <summary>
/// Default <see cref="IHeadlessSaveChangesPipeline"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// Terminal-stage ordering: <see cref="HeadlessLocalEventSaveEntryProcessor"/> and
/// <see cref="HeadlessMessageCollectorSaveEntryProcessor"/> run last so consumer processors can mutate
/// entities before message-collection sees the final state.
/// </para>
/// <para>
/// Cancellation: <c>transaction.CommitAsync</c> has no implicit timeout beyond the supplied
/// <see cref="CancellationToken"/>. Callers should pass a deadline-bounded token when needed.
/// </para>
/// <para>
/// Design note: <see cref="Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor"/> was
/// considered for this pipeline but rejected. The interceptor model cannot defer
/// <c>AcceptAllChanges</c> (the second <c>SaveChanges(false)</c> call needs deferred accept, controlled
/// by the caller via <c>acceptAllChangesOnSuccess: false</c>), and cannot compose ordered
/// <see cref="IHeadlessSaveEntryProcessor"/> stages with guaranteed terminal-stage placement. The
/// pipeline owns the explicit transaction boundary that interceptors don't expose cleanly.
/// </para>
/// </remarks>
internal sealed partial class HeadlessSaveChangesPipeline : IHeadlessSaveChangesPipeline
{
    private readonly IHeadlessMessageDispatcher _messageDispatcher;
    private readonly IReadOnlyList<IHeadlessSaveEntryProcessor> _entryProcessors;
    private readonly IHeadlessAuditPersistence _auditPersistence;
    private readonly ILogger<HeadlessSaveChangesPipeline> _logger;

    public HeadlessSaveChangesPipeline(
        IServiceProvider serviceProvider,
        HeadlessDbContextOptions options,
        IHeadlessMessageDispatcher messageDispatcher,
        IHeadlessAuditPersistence auditPersistence,
        ILogger<HeadlessSaveChangesPipeline>? logger = null
    )
    {
        _messageDispatcher = messageDispatcher;
        _entryProcessors = options.ResolveSaveEntryProcessors(serviceProvider);
        _auditPersistence = auditPersistence;
        _logger = logger ?? NullLogger<HeadlessSaveChangesPipeline>.Instance;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "HeadlessAuditDiscardFailedDuringExceptionPath",
        Level = LogLevel.Error,
        Message = "Audit discard failed during exception path; rethrowing the original SaveChanges exception."
    )]
    private static partial void LogAuditDiscardFailed(ILogger logger, Exception exception);

    public async Task<int> SaveChangesAsync(
        DbContext context,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    )
    {
        // Materialize once — the framework processors don't add new ChangeTracker entries during
        // _ProcessEntries, so a single snapshot is correct for the audit capture too.
        var trackedEntries = _SnapshotEntries(context);
        var saveContext = _ProcessEntries(context, trackedEntries);
        var auditEntries = _auditPersistence.CaptureEntries(trackedEntries);

        var state = new AsyncSaveState(
            context,
            saveContext,
            auditEntries,
            acceptAllChangesOnSuccess,
            baseSaveChangesAsync,
            cancellationToken
        );

        if (context.Database.CurrentTransaction is not null)
        {
            return await _ExecuteWithinCurrentTransactionAsync(state).ConfigureAwait(false);
        }

        if (!_RequiresExplicitTransaction(auditEntries, saveContext))
        {
            var result = await baseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            _CompleteSuccessfulSave(context, saveContext, default, acceptAllChangesOnSuccess);

            return result;
        }

        return await context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(state, _ExecuteWithNewTransactionAsync)
            .ConfigureAwait(false);
    }

    public int SaveChanges(DbContext context, Func<bool, int> baseSaveChanges, bool acceptAllChangesOnSuccess)
    {
#pragma warning disable MA0045 // Sync SaveChanges intentionally wraps EF sync APIs.
        var trackedEntries = _SnapshotEntries(context);
        var saveContext = _ProcessEntries(context, trackedEntries);
        var auditEntries = _auditPersistence.CaptureEntries(trackedEntries);

        var state = new SaveState(context, saveContext, auditEntries, acceptAllChangesOnSuccess, baseSaveChanges);

        if (context.Database.CurrentTransaction is not null)
        {
            return _ExecuteWithinCurrentTransaction(state);
        }

        if (!_RequiresExplicitTransaction(auditEntries, saveContext))
        {
            var result = baseSaveChanges(acceptAllChangesOnSuccess);
            _CompleteSuccessfulSave(context, saveContext, default, acceptAllChangesOnSuccess);

            return result;
        }

        return context.Database.CreateExecutionStrategy().Execute(state, _ExecuteWithNewTransaction);
#pragma warning restore MA0045
    }

    private static IReadOnlyList<EntityEntry> _SnapshotEntries(DbContext context)
    {
        // Single allocation, single ChangeTracker traversal — feeds both _ProcessEntries and the
        // initial audit capture.
        return context.ChangeTracker.Entries().ToArray();
    }

    private HeadlessSaveEntryContext _ProcessEntries(DbContext context, IReadOnlyList<EntityEntry> entries)
    {
        var tenantId = context is IHeadlessDbContext headlessContext ? headlessContext.TenantId : null;
        var saveContext = new HeadlessSaveEntryContext(context, tenantId);

        foreach (var entry in entries)
        {
            foreach (var processor in _entryProcessors)
            {
                processor.Process(entry, saveContext);
            }
        }

        return saveContext;
    }

    private Task<int> _ExecuteWithinCurrentTransactionAsync(AsyncSaveState state)
    {
        // CurrentTransaction was just verified non-null above; null-forgiving here documents that.
        var currentTransaction = state.Context.Database.CurrentTransaction!;
        return _SaveWithinTransactionAsync(state, currentTransaction, commitTransaction: false);
    }

    private int _ExecuteWithinCurrentTransaction(SaveState state)
    {
        // CurrentTransaction was just verified non-null above; null-forgiving here documents that.
        var currentTransaction = state.Context.Database.CurrentTransaction!;
        return _SaveWithinTransaction(state, currentTransaction, commitTransaction: false);
    }

    private async Task<int> _ExecuteWithNewTransactionAsync(AsyncSaveState state)
    {
        await using var transaction = await state
            .Context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, state.CancellationToken)
            .ConfigureAwait(false);

        return await _SaveWithinTransactionAsync(state, transaction, commitTransaction: true).ConfigureAwait(false);
    }

    private int _ExecuteWithNewTransaction(SaveState state)
    {
#pragma warning disable MA0045 // Sync intentionally
        using var transaction = state.Context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
        return _SaveWithinTransaction(state, transaction, commitTransaction: true);
#pragma warning restore MA0045
    }

    private async Task<int> _SaveWithinTransactionAsync(
        AsyncSaveState state,
        IDbContextTransaction transaction,
        bool commitTransaction
    )
    {
        if (commitTransaction)
        {
            _auditPersistence.PrepareForRetry(state.Context);
        }

        HeadlessAuditSaveResult auditSave = default;

        try
        {
            if (state.SaveContext.LocalEmitters.Count > 0)
            {
                await _messageDispatcher
                    .PublishLocalAsync(state.SaveContext.LocalEmitters, transaction, state.CancellationToken)
                    .ConfigureAwait(false);
            }

            var deferAcceptAllChanges = _HasAuditEntries(state.AuditEntries);
            var result = await state
                .BaseSaveChangesAsync(
                    !deferAcceptAllChanges && state.AcceptAllChangesOnSuccess,
                    state.CancellationToken
                )
                .ConfigureAwait(false);

            auditSave = await _auditPersistence
                .ResolveAndPersistAsync(
                    state.Context,
                    state.AuditEntries,
                    state.BaseSaveChangesAsync,
                    state.CancellationToken
                )
                .ConfigureAwait(false);

            if (state.SaveContext.DistributedEmitters.Count > 0)
            {
                await _messageDispatcher
                    .EnqueueDistributedAsync(
                        state.SaveContext.DistributedEmitters,
                        transaction,
                        state.CancellationToken
                    )
                    .ConfigureAwait(false);
            }

            if (commitTransaction)
            {
                await transaction.CommitAsync(state.CancellationToken).ConfigureAwait(false);
            }

            _CompleteSuccessfulSave(state.Context, state.SaveContext, auditSave, state.AcceptAllChangesOnSuccess);

            return result;
        }
        catch (Exception caught)
        {
            try
            {
                _auditPersistence.DiscardEntries(auditSave);
            }
#pragma warning disable CA1031 // Last-resort: a discard failure must not mask the original SaveChanges exception.
            catch (Exception discardFailure)
#pragma warning restore CA1031
            {
                LogAuditDiscardFailed(_logger, discardFailure);
            }

            ExceptionDispatchInfo.Capture(caught).Throw();
            throw; // unreachable; satisfies analyzers
        }
    }

    private int _SaveWithinTransaction(SaveState state, IDbContextTransaction transaction, bool commitTransaction)
    {
#pragma warning disable MA0045 // Sync intentionally.
        if (commitTransaction)
        {
            _auditPersistence.PrepareForRetry(state.Context);
        }

        HeadlessAuditSaveResult auditSave = default;

        try
        {
            if (state.SaveContext.LocalEmitters.Count > 0)
            {
                _messageDispatcher.PublishLocal(state.SaveContext.LocalEmitters, transaction);
            }

            var deferAcceptAllChanges = _HasAuditEntries(state.AuditEntries);
            var result = state.BaseSaveChanges(!deferAcceptAllChanges && state.AcceptAllChangesOnSuccess);
            auditSave = _auditPersistence.ResolveAndPersist(state.Context, state.AuditEntries, state.BaseSaveChanges);

            if (state.SaveContext.DistributedEmitters.Count > 0)
            {
                _messageDispatcher.EnqueueDistributed(state.SaveContext.DistributedEmitters, transaction);
            }

            if (commitTransaction)
            {
                transaction.Commit();
            }

            _CompleteSuccessfulSave(state.Context, state.SaveContext, auditSave, state.AcceptAllChangesOnSuccess);

            return result;
        }
        catch (Exception caught)
        {
            try
            {
                _auditPersistence.DiscardEntries(auditSave);
            }
#pragma warning disable CA1031 // Last-resort: a discard failure must not mask the original SaveChanges exception.
            catch (Exception discardFailure)
#pragma warning restore CA1031
            {
                LogAuditDiscardFailed(_logger, discardFailure);
            }

            ExceptionDispatchInfo.Capture(caught).Throw();
            throw; // unreachable; satisfies analyzers
        }
#pragma warning restore MA0045
    }

    private static bool _RequiresExplicitTransaction(
        IReadOnlyList<AuditLogEntryData>? auditEntries,
        HeadlessSaveEntryContext saveContext
    ) =>
        _HasAuditEntries(auditEntries)
        || saveContext.DistributedEmitters.Count > 0
        || saveContext.LocalEmitters.Count > 0;

    private static bool _HasAuditEntries(IReadOnlyList<AuditLogEntryData>? auditEntries)
    {
        return auditEntries is { Count: > 0 };
    }

    private void _CompleteSuccessfulSave(
        DbContext context,
        HeadlessSaveEntryContext saveContext,
        HeadlessAuditSaveResult auditSave,
        bool acceptAllChangesOnSuccess
    )
    {
        _auditPersistence.CompleteSuccessfulSave(context, auditSave, acceptAllChangesOnSuccess);
        saveContext.ClearEmitterMessages();
    }

    private readonly record struct AsyncSaveState(
        DbContext Context,
        HeadlessSaveEntryContext SaveContext,
        IReadOnlyList<AuditLogEntryData>? AuditEntries,
        bool AcceptAllChangesOnSuccess,
        Func<bool, CancellationToken, Task<int>> BaseSaveChangesAsync,
        CancellationToken CancellationToken
    );

    private readonly record struct SaveState(
        DbContext Context,
        HeadlessSaveEntryContext SaveContext,
        IReadOnlyList<AuditLogEntryData>? AuditEntries,
        bool AcceptAllChangesOnSuccess,
        Func<bool, int> BaseSaveChanges
    );
}
