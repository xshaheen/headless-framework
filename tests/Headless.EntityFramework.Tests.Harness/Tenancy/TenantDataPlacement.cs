// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Tests.Tenancy;

/// <summary>Where a tenancy fixture puts its tables: the database it creates and the contexts' default schema.</summary>
/// <remarks>
/// Suites that must hold under non-default placement (a per-tenant schema or database) run the same tests against a
/// fixture built with another placement instead of copying them.
/// </remarks>
public sealed record TenantDataPlacement(string Database, string? Schema)
{
    public static TenantDataPlacement Default { get; } = new("tenant_conformance", "tenancy");
}

/// <summary>
/// Keys EF's model cache on the context's default schema as well as its type, because EF otherwise builds one model
/// per context type and a second placement in the same process would reuse the first placement's schema.
/// </summary>
public sealed class PlacementModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), (context as HeadlessDbContext)?.DefaultSchema, designTime);
}
