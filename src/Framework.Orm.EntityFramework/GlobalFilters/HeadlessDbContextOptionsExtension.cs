// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Orm.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Orm.EntityFramework.GlobalFilters;

/// <inheritdoc />
public sealed class HeadlessDbContextOptionsExtension : IDbContextOptionsExtension
{
    public void ApplyServices(IServiceCollection services)
    {
        services.TryAddSingleton<IHeadlessEntityModelProcessor, HeadlessEntityModelProcessor>();
        services.TryAddSingleton<IClock, Clock>();
        services.TryAddSingleton<IGuidGenerator, SequentialAtEndGuidGenerator>();
        services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.TryAddSingleton<ICurrentUser, NullCurrentUser>();

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

    public void Validate(IDbContextOptions options) { }

    public DbContextOptionsExtensionInfo Info => new HeadlessOptionsExtensionInfo(this);

    private sealed class HeadlessOptionsExtensionInfo(IDbContextOptionsExtension e) : DbContextOptionsExtensionInfo(e)
    {
        public override string LogFragment => "HeadlessOptionsExtension";

        public override bool IsDatabaseProvider => false;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }

        public override int GetServiceProviderHashCode()
        {
            return 0;
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is HeadlessOptionsExtensionInfo;
        }
    }
}
