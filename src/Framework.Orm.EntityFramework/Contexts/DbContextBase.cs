using System.Data;
using Framework.BuildingBlocks.Domains;
using Framework.BuildingBlocks.Primitives;
using Framework.Orm.EntityFramework.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using File = Framework.BuildingBlocks.Primitives.File;

namespace Framework.Orm.EntityFramework.Contexts;

public abstract class DbContextBase(DbContextOptions options) : DbContext(options)
{
    protected abstract string DefaultSchema { get; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.AddAllPrimitivesValueConvertersMappings();

        configurationBuilder.Properties<decimal?>().HavePrecision(32, 10);
        configurationBuilder.Properties<decimal>().HavePrecision(32, 10);
        configurationBuilder.Properties<Enum>().HaveMaxLength(100).HaveConversion<string>();

        configurationBuilder.Properties<UserId>().HaveConversion<UserIdValueConverter>();
        configurationBuilder.Properties<AccountId>().HaveConversion<AccountIdValueConverter>();
        configurationBuilder.Properties<Month>().HaveConversion<MonthValueConverter>();
        configurationBuilder.Properties<Money>().HaveConversion<MoneyValueConverter>().HavePrecision(32, 10);
        configurationBuilder.Properties<File>().HaveConversion<FileValueConverter>();
        configurationBuilder.Properties<Image>().HaveConversion<ImageValueConverter>();
        configurationBuilder.Properties<Locale>().HaveConversion<LocaleValueConverter, LocaleValueComparer>();
        configurationBuilder
            .Properties<ExtraProperties>()
            .HaveConversion<ExtraPropertiesValueConverter, ExtraPropertiesValueComparer>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DefaultSchema);
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            OnModelEntityType(entityType);
        }
    }

    protected virtual void OnModelEntityType(IMutableEntityType entityType)
    {
        if (entityType.ClrType.IsAssignableTo(typeof(IEntity<long>)))
        {
            entityType.GetProperty(nameof(IEntity<long>.Id)).ValueGenerated = ValueGenerated.Never;
        }

        if (entityType.ClrType.IsAssignableTo(typeof(IEntity<Guid>)))
        {
            entityType.GetProperty(nameof(IEntity<Guid>.Id)).ValueGenerated = ValueGenerated.Never;
        }

        if (entityType.ClrType.IsAssignableTo(typeof(IEntity<string>)))
        {
            entityType.GetProperty(nameof(IEntity<string>.Id)).ValueGenerated = ValueGenerated.Never;
        }
    }

    protected virtual void ProcessEntry(EntityEntry entry)
    {
        UpdateConcurrencyStamp(entry);
    }

    protected virtual void ApplyConceptsForAddedEntity(EntityEntry entry)
    {
        UpdateConcurrencyStamp(entry);
    }

    protected virtual void ApplyConceptsForModifiedEntity(EntityEntry entry, bool forceApply = false)
    {
        var hasAnyModifiedProperties = entry.Properties.Any(x =>
            x is { IsModified: true, Metadata.ValueGenerated: ValueGenerated.Never or ValueGenerated.OnAdd }
        );

        if (forceApply || hasAnyModifiedProperties)
        {
            UpdateConcurrencyStamp(entry);
        }
    }

    protected virtual void ApplyConceptsForDeletedEntity(EntityEntry entry)
    {
        // if (!(entry.Entity is ISoftDelete))
        // {
        //     return;
        // }
        //
        // if (IsHardDeleted(entry))
        // {
        //     return;
        // }
        //
        // entry.Reload();
        // ObjectHelper.TrySetProperty(entry.Entity.As<ISoftDelete>(), x => x.IsDeleted, () => true);
        // SetDeletionAuditProperties(entry);
    }

    protected virtual void UpdateConcurrencyStamp(EntityEntry entry)
    {
        if (entry.Entity is not IHasConcurrencyStamp entity)
        {
            return;
        }

        var property = Entry(entity).Property(x => x.ConcurrencyStamp);

        if (
            entity.ConcurrencyStamp is null
            || string.Equals(property.OriginalValue, property.CurrentValue, StringComparison.Ordinal)
        )
        {
            entity.ConcurrencyStamp = Guid.NewGuid().ToString();
        }
    }

    protected virtual EntityEventReport CreateEventReport()
    {
        var report = new EntityEventReport();

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is not ILocalMessageEmitter localEmitter)
            {
                continue;
            }

            var localMessages = localEmitter.GetLocalMessages();

            if (localMessages.Count > 0)
            {
                report.DomainEvents.AddRange(
                    localMessages.Select(localMessage => new LocalEventEntry(localEmitter, localMessage))
                );

                localEmitter.ClearLocalMessages();
            }

            if (entry.Entity is not IDistributedMessageEmitter distributedEmitter)
            {
                continue;
            }

            var distributedEvents = distributedEmitter.GetDistributedMessages();

            if (distributedEvents.Count > 0)
            {
                report.DistributedEvents.AddRange(
                    distributedEvents.Select(distributedMessage => new DistributedEventEntry(
                        distributedEmitter,
                        distributedMessage
                    ))
                );

                distributedEmitter.ClearDistributedMessages();
            }
        }

        return report;
    }

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

    #region Base Save Changes Implementations

    protected virtual async Task<int> BaseSaveChangesAsync(
        bool acceptAllChangesOnSuccess = true,
        CancellationToken cancellationToken = new()
    )
    {
        var (distributedMessagesEmitters, localMessagesEmitters) = _ProcessEntries();

        if (distributedMessagesEmitters.Count == 0)
        {
            await PublishMessagesAsync(localMessagesEmitters, cancellationToken);

            return await _CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        if (Database.CurrentTransaction is not null)
        {
            await PublishMessagesAsync(localMessagesEmitters, cancellationToken);
            var result = await _CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            await PublishMessagesAsync(distributedMessagesEmitters, Database.CurrentTransaction, cancellationToken);

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

                await PublishMessagesAsync(localMessagesEmitters, cancellationToken);
                var result = await _CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                await PublishMessagesAsync(distributedMessagesEmitters, transaction, cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return result;
            });
    }

    protected virtual int BaseSaveChanges(bool acceptAllChangesOnSuccess = true)
    {
        var (distributedMessagesEmitters, localMessagesEmitters) = _ProcessEntries();

        if (distributedMessagesEmitters.Count == 0)
        {
            PublishMessages(localMessagesEmitters);

            return _CoreSaveChanges(acceptAllChangesOnSuccess);
        }

        if (Database.CurrentTransaction is not null)
        {
            PublishMessages(localMessagesEmitters);
            var result = _CoreSaveChanges(acceptAllChangesOnSuccess);
            PublishMessages(distributedMessagesEmitters, Database.CurrentTransaction);

            return result;
        }

        return Database
            .CreateExecutionStrategy()
            .Execute(() =>
            {
                using var transaction = Database.BeginTransaction(IsolationLevel.ReadCommitted);

                PublishMessages(localMessagesEmitters);
                var result = _CoreSaveChanges(acceptAllChangesOnSuccess);
                PublishMessages(distributedMessagesEmitters, transaction);

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

    #region Messages

    private (List<EmitterDistributedMessages>, List<EmitterLocalMessages>) _ProcessEntries()
    {
        var emittedDistributedMessages = new List<EmitterDistributedMessages>();
        var emittedLocalMessages = new List<EmitterLocalMessages>();

        foreach (var entry in ChangeTracker.Entries())
        {
            ProcessEntry(entry);

            if (entry.Entity is IDistributedMessageEmitter distributedMessageEmitter)
            {
                var messages = distributedMessageEmitter.GetDistributedMessages();

                if (messages.Count > 0)
                {
                    emittedDistributedMessages.Add(new(distributedMessageEmitter, messages));
                }
            }

            if (entry.Entity is ILocalMessageEmitter localMessageEmitter)
            {
                var messages = localMessageEmitter.GetLocalMessages();

                if (messages.Count > 0)
                {
                    emittedLocalMessages.Add(new(localMessageEmitter, messages));
                }
            }
        }

        return (emittedDistributedMessages, emittedLocalMessages);
    }

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
}

public sealed record LocalEventEntry(ILocalMessageEmitter Emitter, ILocalMessage Message);

public sealed record DistributedEventEntry(IDistributedMessageEmitter Emitter, IDistributedMessage Message);

public sealed class EntityEventReport
{
    public List<LocalEventEntry> DomainEvents { get; } = [];

    public List<DistributedEventEntry> DistributedEvents { get; } = [];

    public override string ToString()
    {
        return $"[{nameof(EntityEventReport)}] DomainEvents: {DomainEvents.Count}, DistributedEvents: {DistributedEvents.Count}";
    }
}

public class AbpEntityChangeOptions
{
    /// <summary>Default: true. Publish the EntityUpdatedEvent when any navigation property changes.</summary>
    public bool PublishEntityUpdatedEventWhenNavigationChanges { get; set; } = true;
}
