// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.AuditLog;
using Headless.Orm.EntityFramework.ChangeTrackers;
using Headless.Orm.EntityFramework.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Headless.Orm.EntityFramework;

public abstract class HeadlessIdentityDbContext<
    TUser,
    TRole,
    TKey,
    TUserClaim,
    TUserRole,
    TUserLogin,
    TRoleClaim,
    TUserToken
> : IdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken>
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
    where TUserClaim : IdentityUserClaim<TKey>
    where TUserRole : IdentityUserRole<TKey>
    where TUserLogin : IdentityUserLogin<TKey>
    where TRoleClaim : IdentityRoleClaim<TKey>
    where TUserToken : IdentityUserToken<TKey>
{
    private readonly IHeadlessEntityModelProcessor _entityProcessor;
    private readonly HeadlessEntityFrameworkNavigationModifiedTracker _navigationModifiedTracker = new();

    public abstract string DefaultSchema { get; }

    protected HeadlessIdentityDbContext(IHeadlessEntityModelProcessor entityProcessor, DbContextOptions options)
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
                await AuditSavePipelineHelper
                    .ResolveAndPersistAuditAsync(this, auditEntries, _BaseSaveChangesAsync, cancellationToken)
                    .ConfigureAwait(false);
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
            await PublishMessagesAsync(report.LocalEmitters, Database.CurrentTransaction, cancellationToken);
            var result = await _BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            await AuditSavePipelineHelper
                .ResolveAndPersistAuditAsync(this, auditEntries, _BaseSaveChangesAsync, cancellationToken)
                .ConfigureAwait(false);
            await PublishMessagesAsync(report.DistributedEmitters, Database.CurrentTransaction, cancellationToken);
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

                    await context.PublishMessagesAsync(report.LocalEmitters, transaction, cancellationToken);
                    var result = await context._BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                    await AuditSavePipelineHelper
                        .ResolveAndPersistAuditAsync(
                            context,
                            auditEntries,
                            context._BaseSaveChangesAsync,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    await context.PublishMessagesAsync(report.DistributedEmitters, transaction, cancellationToken);

                    await transaction.CommitAsync(cancellationToken);
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
                AuditSavePipelineHelper.ResolveAndPersistAudit(this, auditEntries, _BaseSaveChanges);
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
            AuditSavePipelineHelper.ResolveAndPersistAudit(this, auditEntries, _BaseSaveChanges);
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
                    AuditSavePipelineHelper.ResolveAndPersistAudit(context, auditEntries, context._BaseSaveChanges);
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
        IsolationLevel isolation = IsolationLevel.ReadCommitted
    )
    {
        var state = (Operation: operation, Isolation: isolation, Context: this);

        return Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async state =>
                {
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(state.Isolation);

                    bool commit;

                    try
                    {
                        commit = await state.Operation();

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync();
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync();

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync();
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                    }
                }
            );
    }

    public Task ExecuteTransactionAsync<TArg>(
        Func<TArg, Task<bool>> operation,
        TArg arg,
        IsolationLevel isolation = IsolationLevel.ReadCommitted
    )
    {
        var state = (Operation: operation, Arg: arg, Isolation: isolation, Context: this);

        return Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async state =>
                {
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(state.Isolation);

                    bool commit;

                    try
                    {
                        commit = await state.Operation(state.Arg);

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync();
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync();

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync();
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                    }
                }
            );
    }

    public Task<TResult?> ExecuteTransactionAsync<TResult>(
        Func<Task<(bool, TResult?)>> operation,
        IsolationLevel isolation = IsolationLevel.ReadCommitted
    )
    {
        var state = (Operation: operation, Isolation: isolation, Context: this);

        return Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async state =>
                {
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(state.Isolation);

                    TResult? result;
                    bool commit;

                    try
                    {
                        (commit, result) = await state.Operation();

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync();
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync();

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync();
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                    }

                    return result;
                }
            );
    }

    public Task<TResult?> ExecuteTransactionAsync<TResult, TArg>(
        Func<TArg, Task<(bool, TResult?)>> operation,
        TArg arg,
        IsolationLevel isolation = IsolationLevel.ReadCommitted
    )
    {
        var state = (Operation: operation, Arg: arg, Isolation: isolation, Context: this);

        return Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async state =>
                {
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(state.Isolation);

                    TResult? result;
                    bool commit;

                    try
                    {
                        (commit, result) = await state.Operation(state.Arg);

                        if (commit)
                        {
                            await state.Context.SaveChangesAsync();
                        }
                    }
                    catch
                    {
                        await transaction.RollbackAsync();

                        throw;
                    }

                    if (commit)
                    {
                        await transaction.CommitAsync();
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                    }

                    return result;
                }
            );
    }

    #endregion

    #region Audit Pipeline

    private ILogger? _AuditLogger =>
        field ??= this.GetService<ILoggerFactory>()
            ?.CreateLogger(
                typeof(
                    HeadlessIdentityDbContext<
                        TUser,
                        TRole,
                        TKey,
                        TUserClaim,
                        TUserRole,
                        TUserLogin,
                        TRoleClaim,
                        TUserToken
                    >
                )
            );

    #endregion

    #region Configure Conventions

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.AddBuildingBlocksPrimitivesConvertersMappings();
    }

    #endregion

    #region Model Creating

    protected override void OnModelCreating(ModelBuilder builder)
    {
        if (!DefaultSchema.IsNullOrWhiteSpace())
        {
            builder.HasDefaultSchema(DefaultSchema);
        }
        base.OnModelCreating(builder);
        _entityProcessor.ProcessModelCreating(builder);
    }

    #endregion
}
