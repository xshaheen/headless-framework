// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace Framework.Orm.EntityFramework.GlobalFilters;

/// <inheritdoc />
public sealed class HeadlessCompiledQueryCacheKeyGenerator(
    ICompiledQueryCacheKeyGenerator inner,
    ICurrentDbContext currentDbContext
) : ICompiledQueryCacheKeyGenerator
{
    public object GenerateCacheKey(Expression query, bool async)
    {
        var cacheKey = inner.GenerateCacheKey(query, async);

        throw new NotImplementedException();
    }
}
