// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Orm.EntityFramework.GlobalFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Framework.Orm.EntityFramework;

public static class HeadlessDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder AddHeadlessDbContextOptionsExtension(
        this DbContextOptionsBuilder optionsBuilder
    )
    {
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            new HeadlessDbContextOptionsExtension()
        );
        return optionsBuilder;
    }

    public static DbContextOptionsBuilder<TContext> AddHeadlessDbContextOptionsExtension<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder
    )
        where TContext : DbContext
    {
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            new HeadlessDbContextOptionsExtension()
        );
        return optionsBuilder;
    }
}
