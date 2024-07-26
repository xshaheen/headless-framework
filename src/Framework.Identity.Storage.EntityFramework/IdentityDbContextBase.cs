using System.Data;
using Framework.BuildingBlocks.Domains;
using Framework.BuildingBlocks.Primitives;
using Framework.BuildingBlocks.Primitives.Extensions;
using Framework.Database.EntityFramework.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
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
    protected IdentityDbContextBase(DbContextOptions options)
        : base(options) { }

    protected abstract string DefaultSchema { get; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.AddAllPrimitivesValueConvertersMappings();

        configurationBuilder.Properties<decimal?>().HavePrecision(32, 10);
        configurationBuilder.Properties<decimal>().HavePrecision(32, 10);
        configurationBuilder.Properties<Money>().HavePrecision(32, 10);
        configurationBuilder.Properties<Enum>().HaveMaxLength(100).HaveConversion<string>();
        configurationBuilder.Properties<Locale>().HaveConversion<LocaleConverter, LocaleComparer>();
        configurationBuilder
            .Properties<ExtraProperties>()
            .HaveConversion<ExtraPropertiesConverter, ExtraPropertiesComparer>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(DefaultSchema);
        base.OnModelCreating(builder);

        foreach (var entityType in builder.Model.GetEntityTypes())
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
        var emitters = _ProcessEntries().ToList();

        if (emitters.Count == 0)
        {
            return await _CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        if (Database.CurrentTransaction is not null)
        {
            var result = await _CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            await PublishMessagesAsync(emitters, Database.CurrentTransaction, cancellationToken);

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

                var result = await _CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                await PublishMessagesAsync(emitters, transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return result;
            });
    }

    protected virtual int BaseSaveChanges(bool acceptAllChangesOnSuccess = true)
    {
        var emitters = _ProcessEntries().ToList();

        if (emitters.Count == 0)
        {
            return _CoreSaveChanges(acceptAllChangesOnSuccess);
        }

        if (Database.CurrentTransaction is not null)
        {
            var result = _CoreSaveChanges(acceptAllChangesOnSuccess);
            PublishMessages(emitters, Database.CurrentTransaction);

            return result;
        }

        return Database
            .CreateExecutionStrategy()
            .Execute(() =>
            {
                using var transaction = Database.BeginTransaction(IsolationLevel.ReadCommitted);
                var result = _CoreSaveChanges(acceptAllChangesOnSuccess);
                PublishMessages(emitters, transaction);
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

    private IEnumerable<EmitterMessages> _ProcessEntries()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            ProcessEntry(entry);

            if (entry.Entity is not IMessageEmitter emitter)
            {
                continue;
            }

            var messages = emitter.GetMessages();

            if (messages.Count > 0)
            {
                yield return new(emitter, messages);
            }
        }
    }

    public abstract Task PublishMessagesAsync(
        List<EmitterMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    );

    public abstract void PublishMessages(List<EmitterMessages> emitters, IDbContextTransaction currentTransaction);

    #endregion
}
