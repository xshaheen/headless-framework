// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Exceptions;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

public static partial class QueryableExtensions
{
    public static async ValueTask<TEntity> FirstByIdAsync<TEntity, TKey>(
        this IQueryable<TEntity> source,
        TKey id,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        var user = await source.FirstOrDefaultAsync(x => x.Id.Equals(id), cancellationToken).AnyContext();

        return user
            ?? throw new EntityNotFoundException(_FirstGenericArgumentTypeName(source.GetType()), id.ToString()!);
    }

    public static async ValueTask<TEntity> FirstByIdAsync<TEntity>(
        this IQueryable<TEntity> source,
        Guid id,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity<Guid>
    {
        var user = await source.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).AnyContext();

        return user ?? throw new EntityNotFoundException(_FirstGenericArgumentTypeName(source.GetType()), id);
    }

    public static async ValueTask<TEntity> FirstByIdAsync<TEntity>(
        this IQueryable<TEntity> source,
        int id,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity<int>
    {
        var user = await source.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).AnyContext();

        return user ?? throw new EntityNotFoundException(_FirstGenericArgumentTypeName(source.GetType()), id);
    }

    public static async ValueTask<TEntity> FirstByIdAsync<TEntity>(
        this IQueryable<TEntity> source,
        long id,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity<long>
    {
        var user = await source.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).AnyContext();

        return user ?? throw new EntityNotFoundException(_FirstGenericArgumentTypeName(source.GetType()), id);
    }

    public static async ValueTask<TEntity> FirstByIdAsync<TEntity>(
        this IQueryable<TEntity> source,
        string id,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity<string>
    {
        var user = await source.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).AnyContext();

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
