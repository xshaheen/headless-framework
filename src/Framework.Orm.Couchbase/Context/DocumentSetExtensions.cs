// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase.Core.Exceptions.KeyValue;
using Couchbase.KeyValue;
using Couchbase.KeyValue.RangeScan;
using Couchbase.Linq;
using Framework.Domains;

namespace Framework.Orm.Couchbase.Context;

public static class DocumentSetExtensions
{
    #region KeyValue Collection Operations

    public static async Task<T?> GetAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        CancellationToken cancellationToken = default
    )
        where T : class, IEntity
    {
        var options = new GetOptions().CancellationToken(cancellationToken);

        return await set.GetAsync(id, options);
    }

    public static async Task<T?> GetAsync<T, TId>(this IDocumentSet<T> set, TId id, GetOptions? options)
        where T : class, IEntity
    {
        try
        {
            var getResult = await set.Collection.GetAsync(id?.ToString()!, options);
            var content = getResult.ContentAs<T>();

            return content;
        }
        catch (DocumentNotFoundException)
        {
            return null;
        }
    }

    public static Task<IExistsResult> ExistsAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        ExistsOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.ExistsAsync(id?.ToString()!, options);
    }

    public static Task<IMutationResult> UpsertAsync<T>(
        this IDocumentSet<T> set,
        T content,
        UpsertOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.UpsertAsync(content.GetKey(), content, options);
    }

    public static Task<IMutationResult> InsertAsync<T>(
        this IDocumentSet<T> set,
        T content,
        InsertOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.InsertAsync(content.GetKey(), content, options);
    }

    public static Task<IMutationResult> ReplaceAsync<T>(
        this IDocumentSet<T> set,
        T content,
        ReplaceOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.ReplaceAsync(content.GetKey(), content, options);
    }

    public static Task RemoveAsync<T, TId>(this IDocumentSet<T> set, TId id, RemoveOptions? options = null)
        where T : class, IEntity
    {
        return set.Collection.RemoveAsync(id?.ToString()!, options);
    }

    public static Task UnlockAsync<T, TId>(this IDocumentSet<T> set, TId id, ulong cas, UnlockOptions? options = null)
        where T : class, IEntity
    {
        return set.Collection.UnlockAsync(id?.ToString()!, cas, options);
    }

    public static Task TouchAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        TimeSpan expiry,
        TouchOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.TouchAsync(id?.ToString()!, expiry, options);
    }

    public static Task<IMutationResult?> TouchWithCasAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        TimeSpan expiry,
        TouchOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.TouchWithCasAsync(id?.ToString()!, expiry, options);
    }

    public static Task<IGetResult> GetAndTouchAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        TimeSpan expiry,
        GetAndTouchOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.GetAndTouchAsync(id?.ToString()!, expiry, options);
    }

    public static Task<IGetResult> GetAndLockAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        TimeSpan expiry,
        GetAndLockOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.GetAndLockAsync(id?.ToString()!, expiry, options);
    }

    public static Task<IGetReplicaResult> GetAnyReplicaAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        GetAnyReplicaOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.GetAnyReplicaAsync(id?.ToString()!, options);
    }

    public static IEnumerable<Task<IGetReplicaResult>> GetAllReplicasAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        GetAllReplicasOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.GetAllReplicasAsync(id?.ToString()!, options);
    }

    public static Task<ILookupInResult> LookupInAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        IEnumerable<LookupInSpec> specs,
        LookupInOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.LookupInAsync(id?.ToString()!, specs, options);
    }

    public static Task<ILookupInReplicaResult> LookupInAnyReplicaAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        IEnumerable<LookupInSpec> specs,
        LookupInAnyReplicaOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.LookupInAnyReplicaAsync(id?.ToString()!, specs, options);
    }

    public static IAsyncEnumerable<ILookupInReplicaResult> LookupInAllReplicasAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        IEnumerable<LookupInSpec> specs,
        LookupInAllReplicasOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.LookupInAllReplicasAsync(id?.ToString()!, specs, options);
    }

    public static Task<IMutateInResult> MutateInAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        IEnumerable<MutateInSpec> specs,
        MutateInOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.MutateInAsync(id?.ToString()!, specs, options);
    }

    public static IAsyncEnumerable<IScanResult> ScanAsync<T>(
        this IDocumentSet<T> set,
        IScanType scanType,
        ScanOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.ScanAsync(scanType, options);
    }

    #endregion
}
