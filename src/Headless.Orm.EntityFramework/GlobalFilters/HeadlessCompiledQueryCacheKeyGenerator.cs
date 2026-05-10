// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Orm.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.Orm.EntityFramework.GlobalFilters;

/// <summary>Adds the current tenant id to EF Core compiled query cache keys.</summary>
public sealed class HeadlessCompiledQueryCacheKeyGenerator(
    ICompiledQueryCacheKeyGenerator inner,
    ICurrentDbContext currentDbContext
) : ICompiledQueryCacheKeyGenerator
{
    public object GenerateCacheKey(Expression query, bool async)
    {
        var innerCacheKey = inner.GenerateCacheKey(query, async);

        if (currentDbContext.Context is HeadlessDbContext db)
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
