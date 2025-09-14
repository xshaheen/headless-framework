// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Orm.EntityFramework.Contexts;
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

        if (currentDbContext.Context is HeadlessDbContext ctx)
        {
            return new HeadlessCompiledQueryCacheKey(cacheKey, ctx.GetCompiledQueryCacheKey());
        }

        return cacheKey;
    }

    private sealed record HeadlessCompiledQueryCacheKey(object CompiledQueryCacheKey, string CurrentFilterCacheKey);
}
