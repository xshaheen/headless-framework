// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Abstractions;
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
        var auditEntries = await _CaptureAuditEntriesAsync(cancellationToken).ConfigureAwait(false);

        // No need to be in transaction if there are no emitters
        if (report.DistributedEmitters.Count == 0 && report.LocalEmitters.Count == 0)
        {
            if (auditEntries is { Count: > 0 })
            {
                await _SaveAuditEntriesAsync(auditEntries, cancellationToken).ConfigureAwait(false);
            }

            var result = await _BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
                .ConfigureAwait(false);
            _navigationModifiedTracker.RemoveModifiedEntityEntries();

            return result;
        }

        // Has current transaction
        if (Database.CurrentTransaction is not null)
        {
            if (auditEntries is { Count: > 0 })
            {
                await _SaveAuditEntriesAsync(auditEntries, cancellationToken).ConfigureAwait(false);
            }

            await PublishMessagesAsync(report.LocalEmitters, Database.CurrentTransaction, cancellationToken)
                .ConfigureAwait(false);
            var result = await _BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
                .ConfigureAwait(false);
            await PublishMessagesAsync(report.DistributedEmitters, Database.CurrentTransaction, cancellationToken)
                .ConfigureAwait(false);
            _navigationModifiedTracker.RemoveModifiedEntityEntries();
            report.ClearEmitterMessages();

            return result;
        }

        return await Database
            .CreateExecutionStrategy()
            .ExecuteAsync(
                (this, report, auditEntries, acceptAllChangesOnSuccess, cancellationToken),
                static async state =>
                {
                    var (context, report, auditEntries, acceptAllChangesOnSuccess, cancellationToken) = state;

                    await using var transaction = await context.Database.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted,
                        cancellationToken
                    );

                    if (auditEntries is { Count: > 0 })
                    {
                        await context._SaveAuditEntriesAsync(auditEntries, cancellationToken).ConfigureAwait(false);
                    }

                    await context
                        .PublishMessagesAsync(report.LocalEmitters, transaction, cancellationToken)
                        .ConfigureAwait(false);
                    var result = await context
                        ._BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
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
        var auditEntries = _CaptureAuditEntries();

        // No need to be in transaction if there are no emitters
        if (report.DistributedEmitters.Count == 0 && report.LocalEmitters.Count == 0)
        {
            if (auditEntries is { Count: > 0 })
            {
                _SaveAuditEntries(auditEntries);
            }

            var result = _BaseSaveChanges(acceptAllChangesOnSuccess);
            _navigationModifiedTracker.RemoveModifiedEntityEntries();

            return result;
        }

        // Has current transaction
        if (Database.CurrentTransaction is not null)
        {
            if (auditEntries is { Count: > 0 })
            {
                _SaveAuditEntries(auditEntries);
            }

            PublishMessages(report.LocalEmitters, Database.CurrentTransaction);
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
                (this, report, auditEntries, acceptAllChangesOnSuccess),
                static state =>
                {
                    var (context, report, auditEntries, acceptAllChangesOnSuccess) = state;

                    using var transaction = context.Database.BeginTransaction(IsolationLevel.ReadCommitted);

                    if (auditEntries is { Count: > 0 })
                    {
                        context._SaveAuditEntries(auditEntries);
                    }

                    context.PublishMessages(report.LocalEmitters, transaction);
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

    #region Audit Capture

    private IReadOnlyList<AuditLogEntryData>? _CaptureAuditEntries()
    {
        var auditCapture = this.GetService<IAuditChangeCapture>();

        if (auditCapture is null)
        {
            return null;
        }

        var currentUser = this.GetService<ICurrentUser>();
        var currentTenant = this.GetService<ICurrentTenant>();
        var correlationIdProvider = this.GetService<ICorrelationIdProvider>();
        var clock = this.GetService<IClock>();
        var timestamp = clock?.UtcNow ?? DateTimeOffset.UtcNow;

        try
        {
            return auditCapture.CaptureChanges(
                ChangeTracker.Entries().Select(static e => (object)e),
                currentUser?.UserId?.ToString(),
                currentUser?.AccountId?.ToString(),
                currentTenant?.Id,
                correlationIdProvider?.CorrelationId,
                timestamp
            );
        }
        catch (Exception ex)
        {
            var logger = this.GetService<ILoggerFactory>()?.CreateLogger<HeadlessDbContext>();
            logger?.LogWarning(ex, "Audit change capture failed. Continuing with entity save.");

            return null;
        }
    }

    private Task<IReadOnlyList<AuditLogEntryData>?> _CaptureAuditEntriesAsync(CancellationToken cancellationToken)
    {
        // Capture is synchronous; wrapping to match async call-sites cleanly.
        return Task.FromResult(_CaptureAuditEntries());
    }

    private void _SaveAuditEntries(IReadOnlyList<AuditLogEntryData> entries)
    {
        var store = this.GetService<IAuditLogStore>();
        store?.Save(entries);
    }

    private async Task _SaveAuditEntriesAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        CancellationToken cancellationToken
    )
    {
        var store = this.GetService<IAuditLogStore>();

        if (store is not null)
        {
            await store.SaveAsync(entries, cancellationToken).ConfigureAwait(false);
        }
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
