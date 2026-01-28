// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Orm.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.Orm.EntityFramework.GlobalFilters;

/// <inheritdoc />
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
            return new HeadlessCompiledQueryCacheKey(innerCacheKey, db.TenantId);
        }

        return innerCacheKey;
    }

    private readonly struct HeadlessCompiledQueryCacheKey(object innerCacheKey, string? tenantId)
        : IEquatable<HeadlessCompiledQueryCacheKey>
    {
        private readonly object _innerCacheKey = innerCacheKey;
        private readonly string? _tenantId = tenantId;

        public override bool Equals(object? obj)
        {
            return obj is HeadlessCompiledQueryCacheKey key && Equals(key);
        }

        public bool Equals(HeadlessCompiledQueryCacheKey other)
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
