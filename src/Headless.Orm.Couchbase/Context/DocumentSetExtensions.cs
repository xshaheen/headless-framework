// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Couchbase.Core.Exceptions.KeyValue;
using Couchbase.KeyValue;
using Couchbase.KeyValue.RangeScan;
using Couchbase.Linq;
using Headless.Domain;

namespace Headless.Couchbase.Context;

/// <summary>
/// Extension methods for <c>IDocumentSet&lt;T&gt;</c> that expose Couchbase key-value operations
/// (get, exists, upsert, insert, replace, remove, lock, touch, sub-document, scan) using the entity's
/// string-keyed document ID derived from <c>IEntity.GetKey()</c>.
/// </summary>
public static class DocumentSetExtensions
{
    #region KeyValue Collection Operations

    /// <summary>Returns the document with the given <paramref name="id"/>, or <see langword="null"/> if not found.</summary>
    public static async Task<T?> GetAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        CancellationToken cancellationToken = default
    )
        where T : class, IEntity
    {
        var options = new GetOptions().CancellationToken(cancellationToken);

        return await set.GetAsync(id, options).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the document with the given <paramref name="id"/> using the supplied <paramref name="options"/>,
    /// or <see langword="null"/> if the document does not exist.
    /// </summary>
    public static async Task<T?> GetAsync<T, TId>(this IDocumentSet<T> set, TId id, GetOptions? options)
        where T : class, IEntity
    {
        try
        {
            var getResult = await set.Collection.GetAsync(id?.ToString()!, options).ConfigureAwait(false);
            var content = getResult.ContentAs<T>();

            return content;
        }
        catch (DocumentNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Checks whether a document with the given <paramref name="id"/> exists in the collection.</summary>
    public static Task<IExistsResult> ExistsAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        ExistsOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.ExistsAsync(id?.ToString()!, options);
    }

    /// <summary>Inserts or replaces the document, keyed by <c>content.GetKey()</c>.</summary>
    public static Task<IMutationResult> UpsertAsync<T>(
        this IDocumentSet<T> set,
        T content,
        UpsertOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.UpsertAsync(content.GetKey(), content, options);
    }

    /// <summary>Inserts the document, keyed by <c>content.GetKey()</c>; fails if a document already exists at that key.</summary>
    public static Task<IMutationResult> InsertAsync<T>(
        this IDocumentSet<T> set,
        T content,
        InsertOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.InsertAsync(content.GetKey(), content, options);
    }

    /// <summary>Replaces an existing document, keyed by <c>content.GetKey()</c>; fails if the document does not exist.</summary>
    public static Task<IMutationResult> ReplaceAsync<T>(
        this IDocumentSet<T> set,
        T content,
        ReplaceOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.ReplaceAsync(content.GetKey(), content, options);
    }

    /// <summary>Removes the document with the given <paramref name="id"/> from the collection.</summary>
    public static Task RemoveAsync<T, TId>(this IDocumentSet<T> set, TId id, RemoveOptions? options = null)
        where T : class, IEntity
    {
        return set.Collection.RemoveAsync(id?.ToString()!, options);
    }

    /// <summary>Unlocks a previously locked document identified by <paramref name="id"/> and <paramref name="cas"/>.</summary>
    public static Task UnlockAsync<T, TId>(this IDocumentSet<T> set, TId id, ulong cas, UnlockOptions? options = null)
        where T : class, IEntity
    {
        return set.Collection.UnlockAsync(id?.ToString()!, cas, options);
    }

    /// <summary>Resets the expiry of the document identified by <paramref name="id"/> to <paramref name="expiry"/>.</summary>
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

    /// <summary>
    /// Resets the expiry of the document identified by <paramref name="id"/> and returns the new CAS value,
    /// or <see langword="null"/> if the document does not exist.
    /// </summary>
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

    /// <summary>Retrieves the document identified by <paramref name="id"/> and simultaneously resets its expiry to <paramref name="expiry"/>.</summary>
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

    /// <summary>Retrieves and pessimistically locks the document identified by <paramref name="id"/> for up to <paramref name="expiry"/>.</summary>
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

    /// <summary>Retrieves the document from any available replica, falling back to the active node.</summary>
    public static Task<IGetReplicaResult> GetAnyReplicaAsync<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        GetAnyReplicaOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.GetAnyReplicaAsync(id?.ToString()!, options);
    }

    /// <summary>Returns tasks that each resolve to the document content from a distinct replica node.</summary>
    public static IEnumerable<Task<IGetReplicaResult>> GetAllReplicas<T, TId>(
        this IDocumentSet<T> set,
        TId id,
        GetAllReplicasOptions? options = null
    )
        where T : class, IEntity
    {
        return set.Collection.GetAllReplicasAsync(id?.ToString()!, options);
    }

    /// <summary>Performs a sub-document read on the document identified by <paramref name="id"/> using the supplied <paramref name="specs"/>.</summary>
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

    /// <summary>Performs a sub-document read against any available replica for the document identified by <paramref name="id"/>.</summary>
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

    /// <summary>Streams sub-document read results from all replica nodes for the document identified by <paramref name="id"/>.</summary>
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

    /// <summary>Performs a sub-document mutation on the document identified by <paramref name="id"/> using the supplied <paramref name="specs"/>.</summary>
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

    /// <summary>Performs a range scan of the collection using the given <paramref name="scanType"/> and streams matching results.</summary>
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
