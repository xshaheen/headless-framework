// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.EntityFramework.CompiledQueryCache;

/// <summary>Adds the current tenant id to EF Core compiled query cache keys.</summary>
[PublicAPI]
public sealed class HeadlessCompiledQueryCacheKeyGenerator(
    ICompiledQueryCacheKeyGenerator inner,
    ICurrentDbContext currentDbContext
) : ICompiledQueryCacheKeyGenerator
{
    /// <summary>
    /// Generates a cache key for the compiled query that incorporates the current tenant identifier
    /// so each tenant's queries are cached independently, preventing cross-tenant query plan reuse.
    /// </summary>
    /// <param name="query">The LINQ query expression to generate a key for.</param>
    /// <param name="async">Whether the query is asynchronous.</param>
    /// <returns>
    /// A composite key wrapping the inner key and the active tenant identifier, or the plain inner key
    /// when the context is not a <see cref="IHeadlessDbContext"/>.
    /// </returns>
    public object GenerateCacheKey(Expression query, bool async)
    {
        var innerCacheKey = inner.GenerateCacheKey(query, async);

        if (currentDbContext.Context is IHeadlessDbContext db)
        {
            return new HeadlessCacheKey(innerCacheKey, db.TenantId);
        }

        return innerCacheKey;
    }

    private readonly struct HeadlessCacheKey(object innerCacheKey, string? tenantId) : IEquatable<HeadlessCacheKey>
    {
        private readonly object _innerCacheKey = innerCacheKey;
        private readonly string? _tenantId = tenantId;

        public override bool Equals(object? obj)
        {
            return obj is HeadlessCacheKey key && Equals(key);
        }

        public bool Equals(HeadlessCacheKey other)
        {
            return _innerCacheKey.Equals(other._innerCacheKey)
                && string.Equals(_tenantId, other._tenantId, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_innerCacheKey, _tenantId);
        }
    }
}
