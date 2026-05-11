// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework.GlobalFilters;

public static class CacheKeyGeneratorSetup
{
    internal static void ReplaceCompiledQueryCacheKeyGenerator(this IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(ICompiledQueryCacheKeyGenerator));

        if (
            descriptor is { ImplementationType: not null }
            && descriptor.ImplementationType != typeof(HeadlessCompiledQueryCacheKeyGenerator)
        )
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
}
