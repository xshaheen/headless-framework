using System.Collections;
using System.Linq.Expressions;
using Couchbase.Core.Exceptions.KeyValue;
using Couchbase.KeyValue;
using Couchbase.KeyValue.RangeScan;
using Couchbase.Linq;
using Couchbase.Linq.Extensions;
using Framework.BuildingBlocks.Domains;

namespace Framework.Database.Couchbase.Context;

/// <summary>Abstraction on top of <see cref="IDocumentSet{T}"/> to provide additional functionality.</summary>
public sealed class CouchbaseDocumentSet<TEntity> : IQueryable<TEntity>
    where TEntity : class, IEntity
{
    private readonly CouchbaseBucketContext _bucketContext;

    public CouchbaseDocumentSet(CouchbaseBucketContext bucketContext, string scopeName, string collectionName)
    {
        _bucketContext = bucketContext;
        ScopeName = scopeName;
        CollectionName = collectionName;
        Expression = Expression.Constant(this);
    }

    public string CollectionName { get; }

    public string ScopeName { get; }

    private IScope Scope => _bucketContext.Bucket.Scope(ScopeName);

    private ICouchbaseCollection Collection => Scope.Collection(CollectionName);

    #region Collection Operations

    public async Task<TEntity?> GetAsync<TId>(TId id, CancellationToken cancellationToken = default)
    {
        var options = new GetOptions().CancellationToken(cancellationToken);

        return await GetAsync(id, options);
    }

    public async Task<TEntity?> GetAsync<TId>(TId id, GetOptions? options = null)
    {
        try
        {
            var getResult = await Collection.GetAsync(id?.ToString()!, options);
            var content = getResult.ContentAs<TEntity>();

            return content;
        }
        catch (DocumentNotFoundException)
        {
            return null;
        }
    }

    public Task<IExistsResult> ExistsAsync<TId>(TId id, ExistsOptions? options = null)
    {
        return Collection.ExistsAsync(id?.ToString()!, options);
    }

    public Task<IMutationResult> UpsertAsync(TEntity content, UpsertOptions? options = null)
    {
        return Collection.UpsertAsync(content.GetKey(), content, options);
    }

    public Task<IMutationResult> InsertAsync(TEntity content, InsertOptions? options = null)
    {
        return Collection.InsertAsync(content.GetKey(), content, options);
    }

    public Task<IMutationResult> ReplaceAsync(TEntity content, ReplaceOptions? options = null)
    {
        return Collection.ReplaceAsync(content.GetKey(), content, options);
    }

    public Task RemoveAsync<TId>(TId id, RemoveOptions? options = null)
    {
        return Collection.RemoveAsync(id?.ToString()!, options);
    }

    public Task UnlockAsync<TId>(TId id, ulong cas, UnlockOptions? options = null)
    {
        return Collection.UnlockAsync(id?.ToString()!, cas, options);
    }

    public Task TouchAsync<TId>(TId id, TimeSpan expiry, TouchOptions? options = null)
    {
        return Collection.TouchAsync(id?.ToString()!, expiry, options);
    }

    public Task<IMutationResult?> TouchWithCasAsync<TId>(TId id, TimeSpan expiry, TouchOptions? options = null)
    {
        return Collection.TouchWithCasAsync(id?.ToString()!, expiry, options);
    }

    public Task<IGetResult> GetAndTouchAsync<TId>(TId id, TimeSpan expiry, GetAndTouchOptions? options = null)
    {
        return Collection.GetAndTouchAsync(id?.ToString()!, expiry, options);
    }

    public Task<IGetResult> GetAndLockAsync<TId>(TId id, TimeSpan expiry, GetAndLockOptions? options = null)
    {
        return Collection.GetAndLockAsync(id?.ToString()!, expiry, options);
    }

    public Task<IGetReplicaResult> GetAnyReplicaAsync<TId>(TId id, GetAnyReplicaOptions? options = null)
    {
        return Collection.GetAnyReplicaAsync(id?.ToString()!, options);
    }

    public IEnumerable<Task<IGetReplicaResult>> GetAllReplicasAsync<TId>(TId id, GetAllReplicasOptions? options = null)
    {
        return Collection.GetAllReplicasAsync(id?.ToString()!, options);
    }

    public Task<ILookupInResult> LookupInAsync<TId>(
        TId id,
        IEnumerable<LookupInSpec> specs,
        LookupInOptions? options = null
    )
    {
        return Collection.LookupInAsync(id?.ToString()!, specs, options);
    }

    public Task<ILookupInReplicaResult> LookupInAnyReplicaAsync<TId>(
        TId id,
        IEnumerable<LookupInSpec> specs,
        LookupInAnyReplicaOptions? options = null
    )
    {
        return Collection.LookupInAnyReplicaAsync(id?.ToString()!, specs, options);
    }

    public IAsyncEnumerable<ILookupInReplicaResult> LookupInAllReplicasAsync<TId>(
        TId id,
        IEnumerable<LookupInSpec> specs,
        LookupInAllReplicasOptions? options = null
    )
    {
        return Collection.LookupInAllReplicasAsync(id?.ToString()!, specs, options);
    }

    public Task<IMutateInResult> MutateInAsync<TId>(
        TId id,
        IEnumerable<MutateInSpec> specs,
        MutateInOptions? options = null
    )
    {
        return Collection.MutateInAsync(id?.ToString()!, specs, options);
    }

    public IAsyncEnumerable<IScanResult> ScanAsync(IScanType scanType, ScanOptions? options = null)
    {
        return Collection.ScanAsync(scanType, options);
    }

    #endregion

    #region IQuerable

    /// <summary>Makes a new queryable for each query. This way the latest settings, such as timeout, are collected.</summary>
    private IQueryable<TEntity> _MakeQueryable() => _bucketContext.Query<TEntity>(ScopeName, CollectionName);

    [MustDisposeResource]
    public IEnumerator<TEntity> GetEnumerator() => _MakeQueryable().GetEnumerator();

    [MustDisposeResource]
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public Type ElementType => typeof(TEntity);

    public Expression Expression { get; }

    public IQueryProvider Provider => _MakeQueryable().Provider;

    public IAsyncEnumerator<TEntity> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return _MakeQueryable().AsAsyncEnumerable().GetAsyncEnumerator(cancellationToken);
    }

    #endregion
}
