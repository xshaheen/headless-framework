using System.Data;
using System.Linq.Expressions;
using System.Security.Claims;
using Framework.Kernel.Domains;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Testing.TestSetup;

public sealed class DbContextExecutor<TDbContext>(IServiceScopeFactory scopeFactory)
    where TDbContext : DbContext
{
    #region Execute Db Context

    public Task ExecuteDbContextAsync(Func<TDbContext, Task> func, ClaimsPrincipal? principal = null)
    {
        return _ExecuteScopeAsync(provider => func(provider.GetRequiredService<TDbContext>()), principal);
    }

    public Task<T> ExecuteDbContextAsync<T>(Func<TDbContext, Task<T>> func, ClaimsPrincipal? principal = null)
    {
        return _ExecuteScopeAsync(provider => func(provider.GetRequiredService<TDbContext>()), principal);
    }

    public Task ExecuteDbContextAsync(Func<TDbContext, ValueTask<bool>> func, ClaimsPrincipal? principal = null)
    {
        return _ExecuteScopeAsync(provider => func(provider.GetRequiredService<TDbContext>()).AsTask(), principal);
    }

    public Task<T> ExecuteDbContextAsync<T>(Func<TDbContext, ValueTask<T>> func, ClaimsPrincipal? principal = null)
    {
        return _ExecuteScopeAsync(provider => func(provider.GetRequiredService<TDbContext>()).AsTask(), principal);
    }

    public Task ExecuteDbContextAsync(Func<TDbContext, IMediator, Task> func, ClaimsPrincipal? principal = null)
    {
        return _ExecuteScopeAsync(
            provider => func(provider.GetRequiredService<TDbContext>(), provider.GetRequiredService<IMediator>()),
            principal
        );
    }

    public Task<T> ExecuteDbContextAsync<T>(
        Func<TDbContext, IMediator, Task<T>> func,
        ClaimsPrincipal? principal = null
    )
    {
        return _ExecuteScopeAsync(
            provider => func(provider.GetRequiredService<TDbContext>(), provider.GetRequiredService<IMediator>()),
            principal
        );
    }

    #endregion

    #region Transaction

    public async Task ExecuteTransactionAsync(
        Func<IServiceProvider, Task<bool>> operation,
        ClaimsPrincipal? claimsPrincipal = null
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope(claimsPrincipal);

        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var state = (Operation: operation, scope.ServiceProvider, Isolation: IsolationLevel.ReadCommitted, Context: db);

        await db
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async state =>
                {
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(state.Isolation);

                    bool commit;

                    try
                    {
                        commit = await state.Operation(state.ServiceProvider);

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

    public Task ExecuteTransactionAsync(Func<TDbContext, Task<bool>> operation, ClaimsPrincipal? claimsPrincipal = null)
    {
        return ExecuteTransactionAsync(
            provider => operation(provider.GetRequiredService<TDbContext>()),
            claimsPrincipal
        );
    }

    public Task ExecuteTransactionAsync(
        Func<TDbContext, IMediator, Task<bool>> operation,
        ClaimsPrincipal? claimsPrincipal = null
    )
    {
        return ExecuteTransactionAsync(
            provider => operation(provider.GetRequiredService<TDbContext>(), provider.GetRequiredService<IMediator>()),
            claimsPrincipal
        );
    }

    public Task ExecuteTransactionAsync(
        Func<TDbContext, ValueTask<bool>> operation,
        ClaimsPrincipal? claimsPrincipal = null
    )
    {
        return ExecuteTransactionAsync(
            provider => operation(provider.GetRequiredService<TDbContext>()).AsTask(),
            claimsPrincipal
        );
    }

    public async Task<TResult?> ExecuteTransactionAsync<TResult>(
        Func<IServiceProvider, Task<(bool Commit, TResult? Result)>> operation,
        ClaimsPrincipal? claimsPrincipal = null
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope(claimsPrincipal);

        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var state = (Operation: operation, scope.ServiceProvider, Isolation: IsolationLevel.ReadCommitted, Context: db);

        return await db
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                state,
                static async state =>
                {
                    await using var transaction = await state.Context.Database.BeginTransactionAsync(state.Isolation);

                    TResult? result;
                    bool commit;

                    try
                    {
                        (commit, result) = await state.Operation(state.ServiceProvider);

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

    public Task<TResult?> ExecuteTransactionAsync<TResult>(
        Func<TDbContext, Task<(bool Commit, TResult? Result)>> operation,
        ClaimsPrincipal? claimsPrincipal = null
    )
    {
        return ExecuteTransactionAsync(
            provider => operation(provider.GetRequiredService<TDbContext>()),
            claimsPrincipal
        );
    }

    public Task<TResult?> ExecuteTransactionAsync<TResult>(
        Func<TDbContext, ValueTask<(bool Commit, TResult? Result)>> operation,
        ClaimsPrincipal? claimsPrincipal = null
    )
    {
        return ExecuteTransactionAsync(
            provider => operation(provider.GetRequiredService<TDbContext>()).AsTask(),
            claimsPrincipal
        );
    }

    public Task<TResult?> ExecuteTransactionAsync<TResult>(
        Func<TDbContext, IMediator, Task<(bool Commit, TResult? Result)>> operation,
        ClaimsPrincipal? claimsPrincipal = null
    )
    {
        return ExecuteTransactionAsync(
            provider => operation(provider.GetRequiredService<TDbContext>(), provider.GetRequiredService<IMediator>()),
            claimsPrincipal
        );
    }

    #endregion

    #region Get

    public Task<T?> FindAsync<T>(params object[] keyValues)
        where T : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<T>().FindAsync(keyValues).AsTask());
    }

    public Task<TEntity> SingleAsync<TEntity>()
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().SingleAsync());
    }

    public Task<TEntity?> SingleOrDefaultAsync<TEntity>()
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().SingleOrDefaultAsync());
    }

    public Task<TEntity> SingleAsync<TEntity>(Expression<Func<TEntity, bool>> predicate)
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().SingleAsync(predicate));
    }

    public Task<TEntity?> SingleOrDefaultAsync<TEntity>(Expression<Func<TEntity, bool>> predicate)
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().SingleOrDefaultAsync(predicate));
    }

    public Task<TEntity> SingleAsync<TEntity>(Guid id)
        where TEntity : class, IEntity<Guid>
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().SingleAsync(e => e.Id == id));
    }

    public Task<TEntity?> SingleOrDefaultAsync<TEntity>(Guid id)
        where TEntity : class, IEntity<Guid>
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().SingleOrDefaultAsync(e => e.Id == id));
    }

    public Task<TEntity> SingleAsync<TEntity>(int id)
        where TEntity : class, IEntity<int>
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().SingleAsync(e => e.Id == id));
    }

    public Task<TEntity?> SingleOrDefaultAsync<TEntity>(int id)
        where TEntity : class, IEntity<int>
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().SingleOrDefaultAsync(e => e.Id == id));
    }

    public Task<TEntity> FirstAsync<TEntity>()
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().FirstAsync());
    }

    public Task<TEntity?> FirstOrDefaultAsync<TEntity>()
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().FirstOrDefaultAsync());
    }

    public Task<TEntity> FirstAsync<TEntity>(Expression<Func<TEntity, bool>> predicate)
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().FirstAsync(predicate));
    }

    public Task<TEntity?> FirstOrDefaultAsync<TEntity>(Expression<Func<TEntity, bool>> predicate)
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().FirstOrDefaultAsync(predicate));
    }

    public Task<TEntity> FirstAsync<TEntity>(Guid id)
        where TEntity : class, IEntity<Guid>
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().FirstAsync(e => e.Id == id));
    }

    public Task<TEntity?> FirstOrDefaultAsync<TEntity>(Guid id)
        where TEntity : class, IEntity<Guid>
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id));
    }

    public Task<TEntity> FirstAsync<TEntity>(int id)
        where TEntity : class, IEntity<int>
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().FirstAsync(e => e.Id == id));
    }

    public Task<TEntity?> FirstOrDefaultAsync<TEntity>(int id)
        where TEntity : class, IEntity<int>
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id));
    }

    public Task<List<TEntity>> GetAllAsync<TEntity>()
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().ToListAsync());
    }

    #endregion

    #region Count

    public Task<int> CountAsync<TEntity>()
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().CountAsync());
    }

    public Task<int> CountAsync<TEntity>(Expression<Func<TEntity, bool>> predicate)
        where TEntity : class, IEntity
    {
        return ExecuteDbContextAsync(db => db.Set<TEntity>().CountAsync(predicate));
    }

    #endregion

    #region Insert

    public Task InsertAsync<T>(params T[] entities)
        where T : class, IEntity
    {
        return ExecuteDbContextAsync(async db =>
        {
            await db.Set<T>().AddRangeAsync(entities);
            await db.SaveChangesAsync();
        });
    }

    #endregion

    #region Helpers

    private async Task _ExecuteScopeAsync(Func<IServiceProvider, Task> func, ClaimsPrincipal? principal = null)
    {
        await using var scope = scopeFactory.CreateAsyncScope(principal);

        await func(scope.ServiceProvider);
    }

    private async Task<T> _ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> func, ClaimsPrincipal? principal = null)
    {
        await using var scope = scopeFactory.CreateAsyncScope(principal);

        return await func(scope.ServiceProvider);
    }

    #endregion
}
