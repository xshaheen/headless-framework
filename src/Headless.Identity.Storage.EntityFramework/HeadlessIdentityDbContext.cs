// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
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

    private ILogger? _AuditLogger => field ??= this.GetService<ILoggerFactory>()?.CreateLogger(GetType());

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
        return await HeadlessSaveChangesRunner
            .ExecuteAsync(
                this,
                _entityProcessor,
                _navigationModifiedTracker.RemoveModifiedEntityEntries,
                (emitters, tx, ct) => PublishMessagesAsync(emitters, tx, ct),
                (emitters, tx, ct) => PublishMessagesAsync(emitters, tx, ct),
                _BaseSaveChangesAsync,
                _AuditLogger,
                acceptAllChangesOnSuccess,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    protected virtual int CoreSaveChanges(bool acceptAllChangesOnSuccess = true)
    {
        return HeadlessSaveChangesRunner.Execute(
            this,
            _entityProcessor,
            _navigationModifiedTracker.RemoveModifiedEntityEntries,
            (emitters, tx) => PublishMessages(emitters, tx),
            (emitters, tx) => PublishMessages(emitters, tx),
            _BaseSaveChanges,
            _AuditLogger,
            acceptAllChangesOnSuccess
        );
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
