// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Framework.Abstractions;
using Framework.Orm.EntityFramework.ChangeTrackers;
using Framework.Orm.EntityFramework.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Framework.Identity.Storage.EntityFramework;

public abstract class IdentityDbContextBase<
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
    private readonly DbContextEntityProcessor _entityProcessor;
    private readonly DbContextModelCreatingProcessor _modelCreatingProcessor;
    private readonly HeadlessEntityFrameworkNavigationModifiedTracker _navigationModifiedTracker;

    public abstract string DefaultSchema { get; }

    public DbContextGlobalFiltersStatus FilterStatus { get; }

    protected IdentityDbContextBase(
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IClock clock,
        DbContextOptions options
    )
        : base(options)
    {
        FilterStatus = new();
        _navigationModifiedTracker = new();
        _entityProcessor = new(currentUser, guidGenerator, clock);
        _modelCreatingProcessor = new(currentTenant, FilterStatus, clock);
        SyncNavigationTracker();
    }

    public void SyncNavigationTracker()
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

        // No need to be in transaction if there are no emitters
        if (report.DistributedEmitters.Count == 0 && report.LocalEmitters.Count == 0)
        {
            var result = await _BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            _navigationModifiedTracker.RemoveModifiedEntityEntries();

            return result;
        }

        // Has current transaction
        if (Database.CurrentTransaction is not null)
        {
            await PublishMessagesAsync(report.LocalEmitters, cancellationToken);
            var result = await _BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            await PublishMessagesAsync(report.DistributedEmitters, Database.CurrentTransaction, cancellationToken);
            _navigationModifiedTracker.RemoveModifiedEntityEntries();
            report.ClearEmitterMessages();

            return result;
        }

        return await Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                (this, report, acceptAllChangesOnSuccess, cancellationToken),
                static async state =>
                {
                    var (context, report, acceptAllChangesOnSuccess, cancellationToken) = state;

                    await using var transaction = await context.Database.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted,
                        cancellationToken
                    );

                    await context.PublishMessagesAsync(report.LocalEmitters, cancellationToken);
                    var result = await context._BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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

        // No need to be in transaction if there are no emitters
        if (report.DistributedEmitters.Count == 0 && report.LocalEmitters.Count == 0)
        {
            var result = _BaseSaveChanges(acceptAllChangesOnSuccess);
            _navigationModifiedTracker.RemoveModifiedEntityEntries();

            return result;
        }

        // Has current transaction
        if (Database.CurrentTransaction is not null)
        {
            PublishMessages(report.LocalEmitters);
            var result = _BaseSaveChanges(acceptAllChangesOnSuccess);
            PublishMessages(report.DistributedEmitters, Database.CurrentTransaction);
            _navigationModifiedTracker.RemoveModifiedEntityEntries();
            report.ClearEmitterMessages();

            return result;
        }

        // No current transaction, create a new one
#pragma warning disable MA0045 // Do not use blocking calls in a sync method (need to make calling method async)
        return Database
            .CreateExecutionStrategy()
            .Execute(
                (this, report, acceptAllChangesOnSuccess),
                static state =>
                {
                    var (context, report, acceptAllChangesOnSuccess) = state;

                    using var transaction = context.Database.BeginTransaction(IsolationLevel.ReadCommitted);

                    context.PublishMessages(report.LocalEmitters);
                    var result = context._BaseSaveChanges(acceptAllChangesOnSuccess);
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
        CancellationToken cancellationToken
    );

    protected abstract void PublishMessages(List<EmitterLocalMessages> emitters);

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
        builder.HasDefaultSchema(DefaultSchema);
        base.OnModelCreating(builder);
        _modelCreatingProcessor.ProcessModelCreating(builder);
    }

    #endregion
}
