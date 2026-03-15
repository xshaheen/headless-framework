// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.AuditLog;
using Headless.Orm.EntityFramework.ChangeTrackers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Headless.Orm.EntityFramework.Contexts;

public abstract class HeadlessDbContext : DbContext
{
    public abstract string DefaultSchema { get; }

    private readonly IHeadlessEntityModelProcessor _entityProcessor;
    private readonly HeadlessEntityFrameworkNavigationModifiedTracker _navigationModifiedTracker = new();

    internal string? TenantId => _entityProcessor.TenantId;

    protected HeadlessDbContext(IHeadlessEntityModelProcessor entityProcessor, DbContextOptions options)
        : base(options)
    {
        _entityProcessor = entityProcessor;
        _SyncNavigationTracker();
    }

    private void _SyncNavigationTracker()
    {
        ChangeTracker.Tracked += _navigationModifiedTracker.ChangeTrackerTracked;
        ChangeTracker.StateChanged += _navigationModifiedTracker.ChangeTrackerStateChanged;
    }

    #region Core Save Changes

    protected virtual async Task<int> CoreSaveChangesAsync(
        bool acceptAllChangesOnSuccess = true,
        CancellationToken cancellationToken = default
    )
    {
        var report = _entityProcessor.ProcessEntries(this);
        var auditEntries = AuditSavePipelineHelper.CaptureAuditEntries(this, _AuditLogger);

        // No emitters — wrap in transaction only when audit entries need persisting
        if (report.DistributedEmitters.Count == 0 && report.LocalEmitters.Count == 0)
        {
            int result;

            if (auditEntries is { Count: > 0 })
            {
                await using var tx = await Database.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
                result = await _BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
                    .ConfigureAwait(false);
                await _ResolveAndPersistAuditAsync(auditEntries, cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await _BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
                    .ConfigureAwait(false);
            }

            _navigationModifiedTracker.RemoveModifiedEntityEntries();

            return result;
        }

        // Has current transaction
        if (Database.CurrentTransaction is not null)
        {
            await PublishMessagesAsync(report.LocalEmitters, Database.CurrentTransaction, cancellationToken)
                .ConfigureAwait(false);
            var result = await _BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
                .ConfigureAwait(false);
            await _ResolveAndPersistAuditAsync(auditEntries, cancellationToken).ConfigureAwait(false);
            await PublishMessagesAsync(report.DistributedEmitters, Database.CurrentTransaction, cancellationToken)
                .ConfigureAwait(false);
            _navigationModifiedTracker.RemoveModifiedEntityEntries();
            report.ClearEmitterMessages();

            return result;
        }

        // No current transaction — use execution strategy with explicit transaction.
        // Audit entries are captured once above; inside the callback we resolve IDs
        // (which may differ across retries) and persist. PrepareForRetry detaches
        // stale AuditLogEntry entities from prior failed attempts.
        return await Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                (this, report, auditEntries, acceptAllChangesOnSuccess, cancellationToken),
                static async state =>
                {
                    var (context, report, auditEntries, acceptAllChangesOnSuccess, cancellationToken) = state;

                    AuditSavePipelineHelper.PrepareForRetry(context);

                    await using var transaction = await context.Database.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted,
                        cancellationToken
                    );

                    await context
                        .PublishMessagesAsync(report.LocalEmitters, transaction, cancellationToken)
                        .ConfigureAwait(false);
                    var result = await context
                        ._BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
                        .ConfigureAwait(false);
                    await context
                        ._ResolveAndPersistAuditAsync(auditEntries, cancellationToken)
                        .ConfigureAwait(false);
                    await context
                        .PublishMessagesAsync(report.DistributedEmitters, transaction, cancellationToken)
                        .ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    context._navigationModifiedTracker.RemoveModifiedEntityEntries();
                    report.ClearEmitterMessages();

                    return result;
                }
            );
    }

    protected virtual int CoreSaveChanges(bool acceptAllChangesOnSuccess = true)
    {
        var report = _entityProcessor.ProcessEntries(this);
        var auditEntries = AuditSavePipelineHelper.CaptureAuditEntries(this, _AuditLogger);

        // No emitters — wrap in transaction only when audit entries need persisting
        if (report.DistributedEmitters.Count == 0 && report.LocalEmitters.Count == 0)
        {
            int result;

            if (auditEntries is { Count: > 0 })
            {
#pragma warning disable MA0045
                using var tx = Database.BeginTransaction();
#pragma warning restore MA0045
                result = _BaseSaveChanges(acceptAllChangesOnSuccess);
                _ResolveAndPersistAudit(auditEntries);
                tx.Commit();
            }
            else
            {
                result = _BaseSaveChanges(acceptAllChangesOnSuccess);
            }

            _navigationModifiedTracker.RemoveModifiedEntityEntries();

            return result;
        }

        // Has current transaction
        if (Database.CurrentTransaction is not null)
        {
            PublishMessages(report.LocalEmitters, Database.CurrentTransaction);
            var result = _BaseSaveChanges(acceptAllChangesOnSuccess);
            _ResolveAndPersistAudit(auditEntries);
            PublishMessages(report.DistributedEmitters, Database.CurrentTransaction);
            _navigationModifiedTracker.RemoveModifiedEntityEntries();
            report.ClearEmitterMessages();

            return result;
        }

        // No current transaction — use execution strategy with explicit transaction.
#pragma warning disable MA0045 // Do not use blocking calls in a sync method (need to make calling method async)
        return Database
            .CreateExecutionStrategy()
            .Execute(
                (this, report, auditEntries, acceptAllChangesOnSuccess),
                static state =>
                {
                    var (context, report, auditEntries, acceptAllChangesOnSuccess) = state;

                    AuditSavePipelineHelper.PrepareForRetry(context);

                    using var transaction = context.Database.BeginTransaction(IsolationLevel.ReadCommitted);

                    context.PublishMessages(report.LocalEmitters, transaction);
                    var result = context._BaseSaveChanges(acceptAllChangesOnSuccess);
                    context._ResolveAndPersistAudit(auditEntries);
                    context.PublishMessages(report.DistributedEmitters, transaction);

                    transaction.Commit();
                    context._navigationModifiedTracker.RemoveModifiedEntityEntries();
                    report.ClearEmitterMessages();

                    return result;
                }
            );
#pragma warning restore MA0045
    }

    #endregion

    #region Overrides Save Changes

    public override int SaveChanges()
    {
        return CoreSaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return CoreSaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new())
    {
        return CoreSaveChangesAsync(cancellationToken: cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = new()
    )
    {
        return CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private Task<int> _BaseSaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private int _BaseSaveChanges(bool acceptAllChangesOnSuccess)
    {
#pragma warning disable MA0045 // Do not use blocking calls in a sync method (need to make calling method async)
        return base.SaveChanges(acceptAllChangesOnSuccess);
#pragma warning restore MA0045
    }

    #endregion

    #region Publish Messages

    protected abstract Task PublishMessagesAsync(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    );

    protected abstract void PublishMessages(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    );

    protected abstract Task PublishMessagesAsync(
        List<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    );

    protected abstract void PublishMessages(
        List<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction
    );

    #endregion

    #region Execute Transaction

    public Task ExecuteTransactionAsync(
        Func<Task<bool>> operation,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Isolation: isolation, Context: this);

        return Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(
                        state.Isolation,
                        ct
                    );

                    bool commit;

                    try
                    {
                        commit = await state.Operation();

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    }
                },
                cancellationToken
            );
    }

    public Task ExecuteTransactionAsync<TArg>(
        Func<TArg, Task<bool>> operation,
        TArg arg,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Arg: arg, Isolation: isolation, Context: this);

        return Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(
                        state.Isolation,
                        ct
                    );

                    bool commit;

                    try
                    {
                        commit = await state.Operation(state.Arg);

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    }
                },
                cancellationToken
            );
    }

    public Task<TResult?> ExecuteTransactionAsync<TResult>(
        Func<Task<(bool, TResult?)>> operation,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Isolation: isolation, Context: this);

        return Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(
                        state.Isolation,
                        ct
                    );

                    TResult? result;
                    bool commit;

                    try
                    {
                        (commit, result) = await state.Operation();

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    }

                    return result;
                },
                cancellationToken
            );
    }

    public Task<TResult?> ExecuteTransactionAsync<TResult, TArg>(
        Func<TArg, Task<(bool, TResult?)>> operation,
        TArg arg,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default
    )
    {
        var state = (Operation: operation, Arg: arg, Isolation: isolation, Context: this);

        return Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async (state, ct) =>
                {
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(
                        state.Isolation,
                        ct
                    );

                    TResult? result;
                    bool commit;

                    try
                    {
                        (commit, result) = await state.Operation(state.Arg);

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    }

                    return result;
                },
                cancellationToken
            );
    }

    #endregion

    #region Audit Pipeline

    private ILogger? _AuditLogger =>
        field ??= this.GetService<ILoggerFactory>()?.CreateLogger<HeadlessDbContext>();

    /// <summary>
    /// Two-phase audit persist: resolves deferred entity IDs (store-generated keys for
    /// Added entities) then adds audit entries to the context and saves them.
    /// </summary>
    private async Task _ResolveAndPersistAuditAsync(
        IReadOnlyList<AuditLogEntryData>? entries,
        CancellationToken cancellationToken
    )
    {
        if (entries is not { Count: > 0 })
            return;

        AuditSavePipelineHelper.ResolveEntityIds(this, entries);
        await AuditSavePipelineHelper.SaveAuditEntriesAsync(this, entries, cancellationToken)
            .ConfigureAwait(false);
        await _BaseSaveChangesAsync(true, cancellationToken).ConfigureAwait(false);
    }

    private void _ResolveAndPersistAudit(IReadOnlyList<AuditLogEntryData>? entries)
    {
        if (entries is not { Count: > 0 })
            return;

        AuditSavePipelineHelper.ResolveEntityIds(this, entries);
        AuditSavePipelineHelper.SaveAuditEntries(this, entries);
#pragma warning disable MA0045
        _BaseSaveChanges(true);
#pragma warning restore MA0045
    }

    #endregion

    #region Configure Conventions

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.AddBuildingBlocksPrimitivesConvertersMappings();
    }

    #endregion

    #region Model Creating

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (!DefaultSchema.IsNullOrWhiteSpace())
        {
            modelBuilder.HasDefaultSchema(DefaultSchema);
        }
        base.OnModelCreating(modelBuilder);
        _entityProcessor.ProcessModelCreating(modelBuilder);
    }

    #endregion
}
