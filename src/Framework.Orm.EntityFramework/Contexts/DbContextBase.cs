using System.Data;
using Framework.Kernel.Domains;
using Framework.Orm.EntityFramework.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Framework.Orm.EntityFramework.Contexts;

public abstract class DbContextBase(DbContextOptions options) : DbContext(options)
{
    protected abstract string DefaultSchema { get; }

    #region Process Saving Entity

    private ProcessBeforeSaveReport _ProcessEntriesBeforeSave()
    {
        var report = new ProcessBeforeSaveReport();

        foreach (var entry in ChangeTracker.Entries())
        {
            ProcessEntryBeforeSave(entry);
            _ProcessMessageEmitters(entry, report);
        }

        return report;
    }

    protected virtual void ProcessEntryBeforeSave(EntityEntry entry)
    {
        switch (entry.State)
        {
            case EntityState.Added:
            {
                if (entry.Entity is IHasConcurrencyStamp entity)
                {
                    entity.ConcurrencyStamp ??= Guid.NewGuid().ToString("N");
                }

                _PublishEntityCreatedEvent(entry.Entity);

                break;
            }
            case EntityState.Modified:
            {
                if (entry.Entity is IHasConcurrencyStamp entity)
                {
                    Entry(entity).Property(x => x.ConcurrencyStamp).OriginalValue = entity.ConcurrencyStamp;
                    entity.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                }

                var hasModifiedProperties =
                    entry.Properties.Any(x =>
                        x is { IsModified: true, Metadata.ValueGenerated: ValueGenerated.Never or ValueGenerated.OnAdd }
                    ) && entry.Properties.Where(x => x.IsModified).All(x => x.Metadata.IsForeignKey());

                if (hasModifiedProperties)
                {
                    _PublishEntityUpdatedEvent(entry.Entity);
                }

                break;
            }
            case EntityState.Deleted:
            {
                _PublishEntityDeletedEvent(entry.Entity);

                break;
            }
        }
    }

    private static void _ProcessMessageEmitters(EntityEntry entry, ProcessBeforeSaveReport report)
    {
        if (entry.Entity is IDistributedMessageEmitter distributedMessageEmitter)
        {
            var messages = distributedMessageEmitter.GetDistributedMessages();

            if (messages.Count > 0)
            {
                report.DistributedEmitters.Add(new(distributedMessageEmitter, messages));
            }
        }

        if (entry.Entity is ILocalMessageEmitter localMessageEmitter)
        {
            var messages = localMessageEmitter.GetLocalMessages();

            if (messages.Count > 0)
            {
                report.LocalEmitters.Add(new(localMessageEmitter, messages));
            }
        }
    }

    private static void _PublishEntityCreatedEvent(object entity)
    {
        if (entity is not ILocalMessageEmitter localEmitter)
        {
            return;
        }

        var genericCreatedEventType = typeof(EntityCreatedEventData<>);
        var createdEventType = genericCreatedEventType.MakeGenericType(entity.GetType());
        var createdEventMessage = (ILocalMessage)Activator.CreateInstance(createdEventType, entity)!;
        localEmitter.AddMessage(createdEventMessage);

        _PublishEntityChangedEvent(entity, localEmitter);
    }

    private static void _PublishEntityUpdatedEvent(object entity)
    {
        if (entity is not ILocalMessageEmitter localEmitter)
        {
            return;
        }

        var genericUpdatedEventType = typeof(EntityUpdatedEventData<>);
        var updatedEventType = genericUpdatedEventType.MakeGenericType(entity.GetType());
        var updatedEventMessage = (ILocalMessage)Activator.CreateInstance(updatedEventType, entity)!;
        localEmitter.AddMessage(updatedEventMessage);

        _PublishEntityChangedEvent(entity, localEmitter);
    }

    private static void _PublishEntityDeletedEvent(object entity)
    {
        if (entity is not ILocalMessageEmitter localEmitter)
        {
            return;
        }

        var genericDeletedEventType = typeof(EntityDeletedEventData<>);
        var deletedEventType = genericDeletedEventType.MakeGenericType(entity.GetType());
        var deletedEventMessage = (ILocalMessage)Activator.CreateInstance(deletedEventType, entity)!;
        localEmitter.AddMessage(deletedEventMessage);

        _PublishEntityChangedEvent(entity, localEmitter);
    }

    private static void _PublishEntityChangedEvent(object entity, ILocalMessageEmitter localEmitter)
    {
        var genericUpdatedEventType = typeof(EntityUpdatedEventData<>);
        var updatedEventType = genericUpdatedEventType.MakeGenericType(entity.GetType());
        var updatedEventMessage = (ILocalMessage)Activator.CreateInstance(updatedEventType, entity)!;
        localEmitter.AddMessage(updatedEventMessage);
    }

    #endregion

    #region Base Save Changes Implementations

    protected virtual async Task<int> BaseSaveChangesAsync(
        bool acceptAllChangesOnSuccess = true,
        CancellationToken cancellationToken = new()
    )
    {
        var report = _ProcessEntriesBeforeSave();

        if (report.DistributedEmitters.Count == 0)
        {
            await PublishMessagesAsync(report.LocalEmitters, cancellationToken);

            return await _CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        if (Database.CurrentTransaction is not null)
        {
            await PublishMessagesAsync(report.LocalEmitters, cancellationToken);
            var result = await _CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            await PublishMessagesAsync(report.DistributedEmitters, Database.CurrentTransaction, cancellationToken);

            return result;
        }

        return await Database
            .CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                await using var transaction = await Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken
                );

                await PublishMessagesAsync(report.LocalEmitters, cancellationToken);
                var result = await _CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                await PublishMessagesAsync(report.DistributedEmitters, transaction, cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return result;
            });
    }

    protected virtual int BaseSaveChanges(bool acceptAllChangesOnSuccess = true)
    {
        var report = _ProcessEntriesBeforeSave();

        if (report.DistributedEmitters.Count == 0)
        {
            PublishMessages(report.LocalEmitters);

            return _CoreSaveChanges(acceptAllChangesOnSuccess);
        }

        if (Database.CurrentTransaction is not null)
        {
            PublishMessages(report.LocalEmitters);
            var result = _CoreSaveChanges(acceptAllChangesOnSuccess);
            PublishMessages(report.DistributedEmitters, Database.CurrentTransaction);

            return result;
        }

        return Database
            .CreateExecutionStrategy()
            .Execute(() =>
            {
                using var transaction = Database.BeginTransaction(IsolationLevel.ReadCommitted);

                PublishMessages(report.LocalEmitters);
                var result = _CoreSaveChanges(acceptAllChangesOnSuccess);
                PublishMessages(report.DistributedEmitters, transaction);

                transaction.Commit();

                return result;
            });
    }

    private Task<int> _CoreSaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private int _CoreSaveChanges(bool acceptAllChangesOnSuccess)
    {
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    #endregion

    #region Public Save Changes Overrides

    public override int SaveChanges()
    {
        return BaseSaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return BaseSaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new())
    {
        return BaseSaveChangesAsync(cancellationToken: cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = new()
    )
    {
        return BaseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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
        configurationBuilder.AddAllPrimitivesValueConvertersMappings();
        configurationBuilder.AddBuildingBlocksPrimitivesConvertersMappings();
    }

    #endregion

    #region Model Creating

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DefaultSchema);
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            _ConfigureValueGenerated(entityType);
            modelBuilder.Entity(entityType.ClrType).ConfigureFrameworkConvention();
        }
    }

    private static void _ConfigureValueGenerated(IMutableEntityType entityType)
    {
        if (entityType.ClrType.IsAssignableTo(typeof(IEntity<int>)))
        {
            entityType.GetProperty(nameof(IEntity<int>.Id)).ValueGenerated = ValueGenerated.Never;
        }

        if (entityType.ClrType.IsAssignableTo(typeof(IEntity<long>)))
        {
            entityType.GetProperty(nameof(IEntity<long>.Id)).ValueGenerated = ValueGenerated.Never;
        }

        if (entityType.ClrType.IsAssignableTo(typeof(IEntity<string>)))
        {
            entityType.GetProperty(nameof(IEntity<string>.Id)).ValueGenerated = ValueGenerated.Never;
        }

        if (entityType.ClrType.IsAssignableTo(typeof(IEntity<Guid>)))
        {
            entityType.GetProperty(nameof(IEntity<Guid>.Id)).ValueGenerated = ValueGenerated.Never;
        }
    }

    #endregion
}
