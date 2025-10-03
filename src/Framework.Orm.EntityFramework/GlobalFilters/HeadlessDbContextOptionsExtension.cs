// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Orm.EntityFramework.GlobalFilters;

/// <inheritdoc />
public sealed class HeadlessDbContextOptionsExtension : IDbContextOptionsExtension
{
    public void ApplyServices(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(ICompiledQueryCacheKeyGenerator));

        if (descriptor is { ImplementationType: not null })
        {
            services.Remove(descriptor);
            services.AddScoped(descriptor.ImplementationType);

            services.Add(
                ServiceDescriptor.Scoped<ICompiledQueryCacheKeyGenerator>(provider =>
                {
                    var generator = ActivatorUtilities.CreateInstance<HeadlessCompiledQueryCacheKeyGenerator>(
                        provider,
                        (ICompiledQueryCacheKeyGenerator)provider.GetRequiredService(descriptor.ImplementationType)
                    );

                    return generator;
                })
            );
        }
    }

    public void Validate(IDbContextOptions options)
    {
        // No validation needed
    }

    public DbContextOptionsExtensionInfo Info => new HeadlessOptionsExtensionInfo(this);

    private sealed class HeadlessOptionsExtensionInfo(IDbContextOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override int GetServiceProviderHashCode()
        {
            return 0;
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is HeadlessOptionsExtensionInfo;
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }

        public override string LogFragment => "HeadlessOptionsExtension";
    }
}
