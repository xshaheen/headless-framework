// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features;
using Headless.Features.Internal;
using Headless.Features.Repositories;
using Headless.Hosting.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupFeatures
{
    extension(IServiceCollection services)
    {
        public HeadlessFeaturesBuilder AddHeadlessFeatures(Action<HeadlessFeaturesSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessFeaturesSetupBuilder(services);
            configure(setup);

            return _AddFeaturesCore(services, setup);
        }

        private static HeadlessFeaturesBuilder _AddFeaturesCore(
            IServiceCollection serviceCollection,
            HeadlessFeaturesSetupBuilder setup
        )
        {
            if (setup.Extensions.Count != 1)
            {
                throw new InvalidOperationException(
                    setup.Extensions.Count == 0
                        ? "Headless.Features requires exactly one storage provider. Call one of `UseEntityFramework`, `UsePostgreSql`, or `UseSqlServer`."
                        : "Headless.Features requires exactly one storage provider. Multiple storage providers were configured."
                );
            }

            serviceCollection.Configure<FeaturesStorageOptions, FeaturesStorageOptionsValidator>(options =>
            {
                options.Schema = setup.StorageOptions.Schema;
                options.FeatureValuesTableName = setup.StorageOptions.FeatureValuesTableName;
                options.FeatureDefinitionsTableName = setup.StorageOptions.FeatureDefinitionsTableName;
                options.FeatureGroupDefinitionsTableName = setup.StorageOptions.FeatureGroupDefinitionsTableName;
            });

            foreach (var extension in setup.Extensions)
            {
                extension.AddServices(serviceCollection);
            }

            return new HeadlessFeaturesBuilder(serviceCollection);
        }
    }
}

[PublicAPI]
public static class SetupFeaturesEntityFramework
{
    extension(HeadlessFeaturesSetupBuilder setup)
    {
        public HeadlessFeaturesSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkFeaturesOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    private sealed class EntityFrameworkFeaturesOptionsExtension(Type dbContextType) : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.TryAddSingleton(
                typeof(IFeatureValueRecordRepository),
                typeof(EfFeatureValueRecordRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddSingleton(
                typeof(IFeatureDefinitionRecordRepository),
                typeof(EfFeatureDefinitionRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton(
                    typeof(IHostedService),
                    typeof(FeaturesEntityValidationStartupGate<>).MakeGenericType(dbContextType)
                )
            );
        }
    }
}
