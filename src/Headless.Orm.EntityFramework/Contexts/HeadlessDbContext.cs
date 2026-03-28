// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Orm.EntityFramework.ChangeTrackers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Headless.Orm.EntityFramework.Contexts;

public abstract class HeadlessDbContext : DbContext
{
    public abstract string DefaultSchema { get; }

    private readonly IHeadlessEntityModelProcessor _entityProcessor;
    private readonly HeadlessEntityFrameworkNavigationModifiedTracker _navigationModifiedTracker = new();

    private ILogger? AuditLogger => field ??= this.GetServiceOrDefault<ILoggerFactory>()?.CreateLogger(GetType());

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
        return await HeadlessSaveChangesRunner
            .ExecuteAsync(
                this,
                _entityProcessor,
                _navigationModifiedTracker,
                PublishMessagesAsync,
                PublishMessagesAsync,
                _BaseSaveChangesAsync,
                AuditLogger,
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
            _navigationModifiedTracker,
            PublishMessages,
            PublishMessages,
            _BaseSaveChanges,
            AuditLogger,
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
        Func<DbContext, CancellationToken, Task> operation,
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

                    try
                    {
                        await state.Operation(state.Context, ct);
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }
                },
                cancellationToken
            );
    }

    public Task ExecuteTransactionAsync<TArg>(
        Func<TArg, DbContext, CancellationToken, Task> operation,
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

                    try
                    {
                        await state.Operation(state.Arg, state.Context, ct);
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }
                },
                cancellationToken
            );
    }

    public Task<TResult> ExecuteTransactionAsync<TResult>(
        Func<DbContext, CancellationToken, Task<TResult>> operation,
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

                    TResult result;

                    try
                    {
                        result = await state.Operation(state.Context, ct);
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
                    }

                    return result;
                },
                cancellationToken
            );
    }

    public Task<TResult> ExecuteTransactionAsync<TResult, TArg>(
        Func<TArg, DbContext, CancellationToken, Task<TResult>> operation,
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

                    TResult result;

                    try
                    {
                        result = await state.Operation(state.Arg, state.Context, ct);
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(ct).ConfigureAwait(false);

                        throw;
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
