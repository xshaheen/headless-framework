// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.AuditLog;
using Headless.EntityFramework.Messaging;
using Headless.EntityFramework.Processors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Headless.EntityFramework;

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

internal sealed class HeadlessSaveChangesPipeline(
    IServiceProvider serviceProvider,
    HeadlessDbContextOptions options,
    IHeadlessMessageDispatcher messageDispatcher
) : IHeadlessSaveChangesPipeline
{
    private readonly IReadOnlyList<IHeadlessSaveEntryProcessor> _entryProcessors = options.ResolveSaveEntryProcessors(
        serviceProvider
    );

    public async Task<int> SaveChangesAsync(
        DbContext context,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    )
    {
        var saveContext = _ProcessEntries(context);
        var auditEntries = HeadlessAuditPersistence.CaptureEntries(context, _GetAuditLogger(context));

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
        var saveContext = _ProcessEntries(context);
        var auditEntries = HeadlessAuditPersistence.CaptureEntries(context, _GetAuditLogger(context));

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

    private HeadlessSaveEntryContext _ProcessEntries(DbContext context)
    {
        var saveContext = new HeadlessSaveEntryContext(context);

        foreach (var entry in context.ChangeTracker.Entries())
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
        var currentTransaction = state.Context.Database.CurrentTransaction!;
        return _SaveWithinTransactionAsync(state, currentTransaction, commitTransaction: false);
    }

    private int _ExecuteWithinCurrentTransaction(SaveState state)
    {
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
            HeadlessAuditPersistence.PrepareForRetry(state.Context);
        }

        HeadlessAuditSaveResult auditSave = default;

        try
        {
            if (state.SaveContext.LocalEmitters.Count > 0)
            {
                await messageDispatcher
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

            auditSave = await HeadlessAuditPersistence
                .ResolveAndPersistAsync(
                    state.Context,
                    state.AuditEntries,
                    state.BaseSaveChangesAsync,
                    state.CancellationToken
                )
                .ConfigureAwait(false);

            if (state.SaveContext.DistributedEmitters.Count > 0)
            {
                await messageDispatcher
                    .PublishDistributedAsync(
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
        catch
        {
            HeadlessAuditPersistence.DetachEntries(auditSave);
            throw;
        }
    }

    private int _SaveWithinTransaction(SaveState state, IDbContextTransaction transaction, bool commitTransaction)
    {
#pragma warning disable MA0045 // Sync intentionally.
        if (commitTransaction)
        {
            HeadlessAuditPersistence.PrepareForRetry(state.Context);
        }

        HeadlessAuditSaveResult auditSave = default;

        try
        {
            if (state.SaveContext.LocalEmitters.Count > 0)
            {
                messageDispatcher.PublishLocal(state.SaveContext.LocalEmitters, transaction);
            }

            var deferAcceptAllChanges = _HasAuditEntries(state.AuditEntries);
            var result = state.BaseSaveChanges(!deferAcceptAllChanges && state.AcceptAllChangesOnSuccess);
            auditSave = HeadlessAuditPersistence.ResolveAndPersist(
                state.Context,
                state.AuditEntries,
                state.BaseSaveChanges
            );

            if (state.SaveContext.DistributedEmitters.Count > 0)
            {
                messageDispatcher.PublishDistributed(state.SaveContext.DistributedEmitters, transaction);
            }

            if (commitTransaction)
            {
                transaction.Commit();
            }

            _CompleteSuccessfulSave(state.Context, state.SaveContext, auditSave, state.AcceptAllChangesOnSuccess);

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
        HeadlessSaveEntryContext saveContext
    ) =>
        _HasAuditEntries(auditEntries)
        || saveContext.DistributedEmitters.Count > 0
        || saveContext.LocalEmitters.Count > 0;

    private static bool _HasAuditEntries(IReadOnlyList<AuditLogEntryData>? auditEntries)
    {
        return auditEntries is { Count: > 0 };
    }

    private static void _CompleteSuccessfulSave(
        DbContext context,
        HeadlessSaveEntryContext saveContext,
        HeadlessAuditSaveResult auditSave,
        bool acceptAllChangesOnSuccess
    )
    {
        HeadlessAuditPersistence.CompleteSuccessfulSave(context, auditSave, acceptAllChangesOnSuccess);
        saveContext.ClearEmitterMessages();
    }

    private static ILogger? _GetAuditLogger(DbContext context)
    {
        return context.GetServiceOrDefault<ILoggerFactory>()?.CreateLogger(context.GetType());
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
