// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Headless.EntityFramework;

public static partial class QueryableExtensions
{
    /// <summary>
    /// Returns the first entity with the specified <paramref name="id"/>, or throws
    /// <c>EntityNotFoundException</c> when no match is found.
    /// </summary>
    /// <typeparam name="TEntity">The entity type implementing <c>IEntity&lt;TKey&gt;</c>.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="source">The source queryable.</param>
    /// <param name="id">The primary key value to look up.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The matching entity.</returns>
    /// <exception cref="Headless.Exceptions.EntityNotFoundException">No entity with the given key exists.</exception>
    public static async ValueTask<TEntity> FirstByIdAsync<TEntity, TKey>(
        this IQueryable<TEntity> source,
        TKey id,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        var user = await source.FirstOrDefaultAsync(x => x.Id.Equals(id), cancellationToken).ConfigureAwait(false);

        return user
            ?? throw new EntityNotFoundException(_FirstGenericArgumentTypeName(source.GetType()), id.ToString()!);
    }

    /// <summary>
    /// Returns the first entity with the specified <see cref="Guid"/> <paramref name="id"/>, or throws
    /// <c>EntityNotFoundException</c> when no match is found.
    /// </summary>
    /// <exception cref="Headless.Exceptions.EntityNotFoundException">No entity with the given key exists.</exception>
    public static async ValueTask<TEntity> FirstByIdAsync<TEntity>(
        this IQueryable<TEntity> source,
        Guid id,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity<Guid>
    {
        var user = await source.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);

        return user ?? throw new EntityNotFoundException(_FirstGenericArgumentTypeName(source.GetType()), id);
    }

    /// <summary>
    /// Returns the first entity with the specified <see cref="int"/> <paramref name="id"/>, or throws
    /// <c>EntityNotFoundException</c> when no match is found.
    /// </summary>
    /// <exception cref="Headless.Exceptions.EntityNotFoundException">No entity with the given key exists.</exception>
    public static async ValueTask<TEntity> FirstByIdAsync<TEntity>(
        this IQueryable<TEntity> source,
        int id,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity<int>
    {
        var user = await source.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);

        return user ?? throw new EntityNotFoundException(_FirstGenericArgumentTypeName(source.GetType()), id);
    }

    /// <summary>
    /// Returns the first entity with the specified <see cref="long"/> <paramref name="id"/>, or throws
    /// <c>EntityNotFoundException</c> when no match is found.
    /// </summary>
    /// <exception cref="Headless.Exceptions.EntityNotFoundException">No entity with the given key exists.</exception>
    public static async ValueTask<TEntity> FirstByIdAsync<TEntity>(
        this IQueryable<TEntity> source,
        long id,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity<long>
    {
        var user = await source.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);

        return user ?? throw new EntityNotFoundException(_FirstGenericArgumentTypeName(source.GetType()), id);
    }

    /// <summary>
    /// Returns the first entity with the specified <see cref="string"/> <paramref name="id"/>, or throws
    /// <c>EntityNotFoundException</c> when no match is found.
    /// </summary>
    /// <exception cref="Headless.Exceptions.EntityNotFoundException">No entity with the given key exists.</exception>
    public static async ValueTask<TEntity> FirstByIdAsync<TEntity>(
        this IQueryable<TEntity> source,
        string id,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity<string>
    {
        var user = await source.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);

        return user ?? throw new EntityNotFoundException(_FirstGenericArgumentTypeName(source.GetType()), id);
    }

    private static string _FirstGenericArgumentTypeName(Type type)
    {
        var genericArguments = type.GetGenericArguments();
        var genericArgument = genericArguments[0];
        var genericArgumentTypeName = genericArgument.Name;

        return genericArgumentTypeName;
    }
}
