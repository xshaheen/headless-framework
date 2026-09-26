// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using AwesomeAssertions;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Testing.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Headless.EntityFramework.Testing;

/// <summary>
/// Asserts that a row tenant A owns stays out of tenant B's reach through EF Core: tenant B's query comes back empty
/// and tenant B's update and delete are refused with <see cref="CrossTenantWriteException"/>.
/// </summary>
/// <remarks>
/// <para>
/// Seed the row as <see cref="TenantWorld.TenantA"/> first. Every assertion starts with an owner control, reading
/// the row as tenant A, and fails when that read finds nothing: a probe for a row that does not exist passes for
/// the wrong reason.
/// </para>
/// <para>
/// <c>createContext</c> is called inside each tenant scope and must return a new context each time; the assertion
/// disposes it. A context whose schema or database depends on the current tenant therefore gets the probing
/// tenant's placement. Tenant B's probes match tenant A's row by key and tenant, so a row tenant B owns under the
/// same key is not tenant A's. When tenant B's context cannot reach tenant A's row with every query filter off, the
/// data is physically isolated and the write check passes without writing; when another query filter hides it, the
/// write check fails, because the tenant write guard covers only tenant-owned entities.
/// </para>
/// </remarks>
[PublicAPI]
public static class TenantIsolationDbAssertions
{
    /// <summary>
    /// Runs <see cref="ShouldNotReadAcrossTenantsAsync{TEntity}"/> and then
    /// <see cref="ShouldRefuseWritesAcrossTenantsAsync{TEntity}"/> for the row <paramref name="key"/> identifies.
    /// </summary>
    /// <typeparam name="TEntity">A tenant-owned entity type with a single-column primary key.</typeparam>
    /// <param name="world">The two tenants; tenant A owns the row.</param>
    /// <param name="createContext">Returns a new context for the current tenant; the assertion disposes it.</param>
    /// <param name="key">The primary key of a row tenant A owns, of the key property's CLR type.</param>
    /// <param name="cancellationToken">Cancels the queries and saves.</param>
    /// <returns>A task that faults with an assertion failure when tenant B can read or write the row.</returns>
    /// <exception cref="ArgumentException">The entity has a composite key, or <paramref name="key"/> has the wrong type.</exception>
    public static async Task ShouldNotSeeAcrossTenantsAsync<TEntity>(
        TenantWorld world,
        Func<DbContext> createContext,
        object key,
        CancellationToken cancellationToken = default
    )
        where TEntity : class
    {
        _CheckArguments(world, createContext, key);
        await _EnsureOwnerReadsAsync<TEntity>(world, createContext, key, cancellationToken).ConfigureAwait(false);
        await _EnsureNotReadAcrossTenantsAsync<TEntity>(world, createContext, key, cancellationToken)
            .ConfigureAwait(false);
        await _EnsureWritesRefusedAsync<TEntity>(world, createContext, key, mutate: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asserts that tenant A reads the row <paramref name="key"/> identifies and tenant B does not.
    /// </summary>
    /// <typeparam name="TEntity">A tenant-owned entity type with a single-column primary key.</typeparam>
    /// <param name="world">The two tenants; tenant A owns the row.</param>
    /// <param name="createContext">Returns a new context for the current tenant; the assertion disposes it.</param>
    /// <param name="key">The primary key of a row tenant A owns, of the key property's CLR type.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>A task that faults with an assertion failure when tenant B reads the row.</returns>
    /// <exception cref="ArgumentException">The entity has a composite key, or <paramref name="key"/> has the wrong type.</exception>
    public static async Task ShouldNotReadAcrossTenantsAsync<TEntity>(
        TenantWorld world,
        Func<DbContext> createContext,
        object key,
        CancellationToken cancellationToken = default
    )
        where TEntity : class
    {
        _CheckArguments(world, createContext, key);
        await _EnsureOwnerReadsAsync<TEntity>(world, createContext, key, cancellationToken).ConfigureAwait(false);
        await _EnsureNotReadAcrossTenantsAsync<TEntity>(world, createContext, key, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asserts that tenant A reads the row <paramref name="key"/> identifies, and that tenant B's update and delete of
    /// it each throw <see cref="CrossTenantWriteException"/>.
    /// </summary>
    /// <typeparam name="TEntity">A tenant-owned entity type with a single-column primary key.</typeparam>
    /// <param name="world">The two tenants; tenant A owns the row.</param>
    /// <param name="createContext">Returns a new context for the current tenant; the assertion disposes it.</param>
    /// <param name="key">The primary key of a row tenant A owns, of the key property's CLR type.</param>
    /// <param name="mutate">
    /// Changes the loaded row for the update attempt. When omitted, or when it changes nothing, the row is marked
    /// modified as it is.
    /// </param>
    /// <param name="cancellationToken">Cancels the queries and saves.</param>
    /// <returns>A task that faults with an assertion failure when a tenant B write is saved or fails another way.</returns>
    /// <exception cref="ArgumentException">The entity has a composite key, or <paramref name="key"/> has the wrong type.</exception>
    public static async Task ShouldRefuseWritesAcrossTenantsAsync<TEntity>(
        TenantWorld world,
        Func<DbContext> createContext,
        object key,
        Action<TEntity>? mutate = null,
        CancellationToken cancellationToken = default
    )
        where TEntity : class
    {
        _CheckArguments(world, createContext, key);
        await _EnsureOwnerReadsAsync<TEntity>(world, createContext, key, cancellationToken).ConfigureAwait(false);
        await _EnsureWritesRefusedAsync(world, createContext, key, mutate, cancellationToken).ConfigureAwait(false);
    }

    private static async Task _EnsureNotReadAcrossTenantsAsync<TEntity>(
        TenantWorld world,
        Func<DbContext> createContext,
        object key,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        using var _ = world.AsTenantB();

        if (await _IsVisibleAsync<TEntity>(createContext, key, world.TenantA, cancellationToken).ConfigureAwait(false))
        {
            _Fail(
                $"Expected {world.TenantB} not to read {world.TenantA}'s {_Name<TEntity>()} {_Format(key)}, but "
                    + $"{world.TenantB} read {world.TenantA}'s {_Name<TEntity>()} {_Format(key)}. The entity has no "
                    + "tenant query filter: mark it IsTenantOwned(...) or implement IMultiTenant, and do not ignore "
                    + "HeadlessQueryFilters.MultiTenancyFilter on this path."
            );
        }
    }

    private static async Task _EnsureWritesRefusedAsync<TEntity>(
        TenantWorld world,
        Func<DbContext> createContext,
        object key,
        Action<TEntity>? mutate,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        using var _ = world.AsTenantB();

        var update = _Create(createContext);

        await using (update.ConfigureAwait(false))
        {
            var entity = await _LoadAcrossTenantsAsync<TEntity>(world, update, key, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                await _EnsureNotHiddenByOtherFiltersAsync<TEntity>(world, update, key, cancellationToken)
                    .ConfigureAwait(false);

                // The owner reads the row but tenant B's context cannot reach it with every filter off: the row lives
                // in a schema or database tenant B's context does not use, so no write can target it.
                return;
            }

            mutate?.Invoke(entity);
            var entry = update.Entry(entity);
            update.ChangeTracker.DetectChanges();

            if (entry.State == EntityState.Unchanged)
            {
                entry.State = EntityState.Modified;
            }

            await _ExpectRefusedAsync<TEntity>(world, update, "update", key, cancellationToken).ConfigureAwait(false);
        }

        var delete = _Create(createContext);

        await using (delete.ConfigureAwait(false))
        {
            var entity = await _LoadAcrossTenantsAsync<TEntity>(world, delete, key, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                _Fail(
                    $"Expected {world.TenantB}'s context to reach {world.TenantA}'s {_Name<TEntity>()} {_Format(key)} "
                        + "for the delete attempt as it did for the update attempt, but the row was gone."
                );
            }

            delete.Remove(entity);
            await _ExpectRefusedAsync<TEntity>(world, delete, "delete", key, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task _EnsureOwnerReadsAsync<TEntity>(
        TenantWorld world,
        Func<DbContext> createContext,
        object key,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        using var _ = world.AsTenantA();

        if (!await _IsVisibleAsync<TEntity>(createContext, key, owner: null, cancellationToken).ConfigureAwait(false))
        {
            _Fail(
                $"Expected owner {world.TenantA} to read {_Name<TEntity>()} {_Format(key)}, but it found nothing. "
                    + "Seed the row inside world.AsTenantA() first; a cross-tenant check against a row that does "
                    + "not exist proves nothing."
            );
        }
    }

    private static async Task<bool> _IsVisibleAsync<TEntity>(
        Func<DbContext> createContext,
        object key,
        string? owner,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        var db = _Create(createContext);

        await using (db.ConfigureAwait(false))
        {
            // No tracking, so a row cached by an earlier query cannot stand in for what the filter returns.
            return await _ByKey<TEntity>(db, key, owner)
                .AsNoTracking()
                .AnyAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static Task<TEntity?> _LoadAcrossTenantsAsync<TEntity>(
        TenantWorld world,
        DbContext db,
        object key,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        // Only the tenant filter is lifted, so the row is reachable exactly when the tenant boundary alone hides it.
        return _ByKey<TEntity>(db, key, world.TenantA)
            .IgnoreMultiTenancyFilter()
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task _EnsureNotHiddenByOtherFiltersAsync<TEntity>(
        TenantWorld world,
        DbContext db,
        object key,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        // The owner read passed with every filter on, so a filter that hides the row from tenant B alone is a tenant
        // filter the framework does not own; its write guard never sees the entity, and "unreachable" would be a lie.
        var hidden = await _ByKey<TEntity>(db, key, world.TenantA)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        if (hidden)
        {
            _Fail(
                $"Expected {world.TenantB}'s context either to reach {world.TenantA}'s {_Name<TEntity>()} "
                    + $"{_Format(key)} with HeadlessQueryFilters.MultiTenancyFilter lifted or not to hold it at all, but "
                    + "another query filter hides it. The tenant write guard covers only entities marked "
                    + "IsTenantOwned(...) or implementing IMultiTenant, so this check cannot prove writes are refused."
            );
        }
    }

    private static async Task _ExpectRefusedAsync<TEntity>(
        TenantWorld world,
        DbContext db,
        string operation,
        object key,
        CancellationToken cancellationToken
    )
    {
        var target = $"{world.TenantA}'s {_Name<TEntity>()} {_Format(key)}";

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CrossTenantWriteException)
        {
            return;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _Fail(
                $"Expected {world.TenantB}'s {operation} of {target} to throw {nameof(CrossTenantWriteException)}, but "
                    + $"it threw {e.GetType().Name}: {e.Message}"
            );
        }

        _Fail(
            $"Expected {world.TenantB}'s {operation} of {target} to throw {nameof(CrossTenantWriteException)}, but it "
                + "was saved. The tenant write guard is off: enable it with "
                + "AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()))."
        );
    }

    // With an owner, a row is identified by key and tenant: under per-tenant placement keys repeat across tenants, and
    // tenant B's own row with tenant A's key is not tenant A's row.
    private static IQueryable<TEntity> _ByKey<TEntity>(DbContext db, object key, string? owner = null)
        where TEntity : class
    {
        var entityType =
            db.Model.FindEntityType(typeof(TEntity))
            ?? throw new ArgumentException(
                $"{typeof(TEntity).Name} is not mapped by {db.GetType().Name}.",
                nameof(key)
            );
        var primaryKey =
            entityType.FindPrimaryKey()
            ?? throw new ArgumentException($"{typeof(TEntity).Name} has no primary key.", nameof(key));

        if (primaryKey.Properties.Count != 1)
        {
            throw new ArgumentException(
                $"{typeof(TEntity).Name} has a composite primary key; the tenant-isolation assertions identify a row "
                    + "by a single key value.",
                nameof(key)
            );
        }

        var property = primaryKey.Properties[0];

        if (key.GetType() != property.ClrType)
        {
            throw new ArgumentException(
                $"The key for {typeof(TEntity).Name} must be a {property.ClrType.Name}, but a {key.GetType().Name} was given.",
                nameof(key)
            );
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var read = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [property.ClrType],
            parameter,
            Expression.Constant(property.Name)
        );
        Expression body = Expression.Equal(read, Expression.Constant(key, property.ClrType));

        if (
            owner is not null
            && entityType.GetTenantPropertyName() is { } tenantName
            && entityType.FindProperty(tenantName) is { } tenantProperty
        )
        {
            var tenantType = tenantProperty.ClrType;
            var tenant = Expression.Call(
                typeof(EF),
                nameof(EF.Property),
                [tenantType],
                parameter,
                Expression.Constant(tenantName)
            );
            body = Expression.AndAlso(body, Expression.Equal(tenant, Expression.Constant(owner, tenantType)));
        }

        var predicate = Expression.Lambda<Func<TEntity, bool>>(body, parameter);

        return db.Set<TEntity>().Where(predicate);
    }

    private static DbContext _Create(Func<DbContext> createContext) =>
        createContext() ?? throw new InvalidOperationException("createContext returned null.");

    private static void _CheckArguments(TenantWorld world, Func<DbContext> createContext, object key)
    {
        Argument.IsNotNull(world);
        Argument.IsNotNull(createContext);
        Argument.IsNotNull(key);
    }

    private static string _Name<TEntity>() => typeof(TEntity).Name;

    private static string _Format(object key) => key.ToInvariantString() ?? "";

    [DoesNotReturn]
    private static void _Fail(string message)
    {
        AssertionEngine.TestFramework.Throw(message);
        throw new InvalidOperationException(message);
    }
}
