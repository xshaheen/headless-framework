// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Headless.Orm.EntityFramework.Contexts;

internal static class HeadlessSaveChangesRunner
{
    public static async Task<int> ExecuteAsync(
        DbContext context,
        IHeadlessEntityModelProcessor entityProcessor,
        Action cleanup,
        Func<List<EmitterLocalMessages>, IDbContextTransaction, CancellationToken, Task> publishLocalAsync,
        Func<List<EmitterDistributedMessages>, IDbContextTransaction, CancellationToken, Task> publishDistributedAsync,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    )
    {
        var report = entityProcessor.ProcessEntries(context);
        var auditEntries = AuditSavePipelineHelper.CaptureAuditEntries(context, _GetAuditLogger(context));

        var state = new AsyncSaveState(
            context,
            report,
            auditEntries,
            acceptAllChangesOnSuccess,
            publishLocalAsync,
            publishDistributedAsync,
            baseSaveChangesAsync,
            cleanup,
            cancellationToken
        );

        if (context.Database.CurrentTransaction is not null)
        {
            return await _ExecuteWithinCurrentTransactionAsync(state).ConfigureAwait(false);
        }

        var requiresExplicitTransaction =
            auditEntries is { Count: > 0 } || report.DistributedEmitters.Count > 0 || report.LocalEmitters.Count > 0;

        if (!requiresExplicitTransaction)
        {
            var result = await baseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            _CompleteSuccessfulSave(report, cleanup);
            return result;
        }

        return await context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async saveState => await _ExecuteWithNewTransactionAsync(saveState).ConfigureAwait(false)
            )
            .ConfigureAwait(false);
    }

    public static int Execute(
        DbContext context,
        IHeadlessEntityModelProcessor entityProcessor,
        Action cleanup,
        Action<List<EmitterLocalMessages>, IDbContextTransaction> publishLocal,
        Action<List<EmitterDistributedMessages>, IDbContextTransaction> publishDistributed,
        Func<bool, int> baseSaveChanges,
        bool acceptAllChangesOnSuccess
    )
    {
#pragma warning disable MA0045 // Sync SaveChanges intentionally wraps EF sync APIs.
        var report = entityProcessor.ProcessEntries(context);
        var auditEntries = AuditSavePipelineHelper.CaptureAuditEntries(context, _GetAuditLogger(context));

        var state = new SaveState(
            context,
            report,
            auditEntries,
            acceptAllChangesOnSuccess,
            publishLocal,
            publishDistributed,
            baseSaveChanges,
            cleanup
        );

        if (context.Database.CurrentTransaction is not null)
        {
            return _ExecuteWithinCurrentTransaction(state);
        }

        var requiresExplicitTransaction =
            auditEntries is { Count: > 0 } || report.DistributedEmitters.Count > 0 || report.LocalEmitters.Count > 0;

        if (!requiresExplicitTransaction)
        {
            var result = baseSaveChanges(acceptAllChangesOnSuccess);
            _CompleteSuccessfulSave(report, cleanup);
            return result;
        }

        return context
            .Database.CreateExecutionStrategy()
            .Execute(state, static saveState => _ExecuteWithNewTransaction(saveState));
#pragma warning restore MA0045
    }

    private static async Task<int> _ExecuteWithinCurrentTransactionAsync(AsyncSaveState state)
    {
        var currentTransaction =
            state.Context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Current transaction is required.");

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

        _CompleteSuccessfulSave(state.Report, state.Cleanup);

        return result;
    }

    private static int _ExecuteWithinCurrentTransaction(SaveState state)
    {
        var currentTransaction =
            state.Context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Current transaction is required.");

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

        _CompleteSuccessfulSave(state.Report, state.Cleanup);

        return result;
    }

    private static async Task<int> _ExecuteWithNewTransactionAsync(AsyncSaveState state)
    {
        AuditSavePipelineHelper.PrepareForRetry(state.Context);

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
        _CompleteSuccessfulSave(state.Report, state.Cleanup);

        return result;
    }

    private static int _ExecuteWithNewTransaction(SaveState state)
    {
        AuditSavePipelineHelper.PrepareForRetry(state.Context);

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
        _CompleteSuccessfulSave(state.Report, state.Cleanup);

        return result;
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

        AuditSavePipelineHelper.ResolveEntityIds(context, entries);
        await AuditSavePipelineHelper.SaveAuditEntriesAsync(context, entries, cancellationToken).ConfigureAwait(false);
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

        AuditSavePipelineHelper.ResolveEntityIds(context, entries);
        AuditSavePipelineHelper.SaveAuditEntries(context, entries);
        baseSaveChanges(true);
    }

    private static ILogger? _GetAuditLogger(DbContext context)
    {
        return context.GetService<ILoggerFactory>()?.CreateLogger(context.GetType());
    }

    private static void _CompleteSuccessfulSave(ProcessBeforeSaveReport report, Action cleanup)
    {
        cleanup();
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
        Action Cleanup,
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
        Action Cleanup
    );
}
