// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Orm.EntityFramework.Contexts;
using Framework.Orm.EntityFramework.GlobalFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Orm.EntityFramework;

[PublicAPI]
public static class HeadlessServiceCollectionExtensions
{
    public static IServiceCollection AddHeadlessDbContext<TDbContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? optionsAction,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
    )
        where TDbContext : HeadlessDbContext
    {
        services.AddHeadlessDbContextServices();

        return services.AddHeadlessDbContext<TDbContext>(
            (_, ob) => optionsAction?.Invoke(ob),
            contextLifetime,
            optionsLifetime
        );
    }

    public static IServiceCollection AddHeadlessDbContext<TDbContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
    )
        where TDbContext : HeadlessDbContext
    {
        services.AddHeadlessDbContextServices();

        services.AddDbContext<TDbContext>(
            (serviceProvider, optionsBuilder) =>
            {
                optionsAction?.Invoke(serviceProvider, optionsBuilder);
                optionsBuilder.AddHeadlessExtension();
            },
            contextLifetime,
            optionsLifetime
        );

        return services;
    }

    extension(IServiceCollection services)
    {
        public void AddHeadlessDbContextServices()
        {
            services.TryAddSingleton<IHeadlessEntityModelProcessor, HeadlessEntityModelProcessor>();
            services.TryAddSingleton<IClock, Clock>();
            services.TryAddSingleton<IGuidGenerator, SequentialAtEndGuidGenerator>();
            services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
            services.TryAddSingleton<ICurrentUser, NullCurrentUser>();
            services._ReplaceCompiledQueryCacheKeyGenerator();
        }

        private void _ReplaceCompiledQueryCacheKeyGenerator()
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
}
