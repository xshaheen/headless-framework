// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

public static class SetupOptionsExtension
{
    public static DbContextOptionsBuilder AddHeadlessExtension(this DbContextOptionsBuilder optionsBuilder)
    {
        Argument.IsNotNull(optionsBuilder);

        var ext = new HeadlessDbContextOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(ext);

        return optionsBuilder;
    }

    public static DbContextOptionsBuilder<TContext> AddHeadlessExtension<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder
    )
        where TContext : DbContext
    {
        Argument.IsNotNull(optionsBuilder);

        var ext = new HeadlessDbContextOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(ext);

        return optionsBuilder;
    }
}

/// <summary>Registers Headless EF Core services through DbContext options.</summary>
public sealed class HeadlessDbContextOptionsExtension : IDbContextOptionsExtension
{
    public void ApplyServices(IServiceCollection services) => services.AddHeadlessDbContextServices();

    public void Validate(IDbContextOptions options) { }

    public DbContextOptionsExtensionInfo Info => new HeadlessOptionsExtensionInfo(this);

    private sealed class HeadlessOptionsExtensionInfo(IDbContextOptionsExtension e) : DbContextOptionsExtensionInfo(e)
    {
        public override string LogFragment => "HeadlessOptionsExtension";

        public override bool IsDatabaseProvider => false;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is HeadlessOptionsExtensionInfo;
        }
    }
}
