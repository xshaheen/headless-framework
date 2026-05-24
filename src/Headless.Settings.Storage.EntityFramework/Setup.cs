// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Storage;
using Headless.Settings;
using Headless.Settings.Internal;
using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupSettings
{
    extension(IServiceCollection services)
    {
        public HeadlessSettingsBuilder AddHeadlessSettings(Action<HeadlessSettingsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessSettingsSetupBuilder(services);
            configure(setup);

            return _RegisterCoreSettingsServices(services, setup);
        }

        private static HeadlessSettingsBuilder _RegisterCoreSettingsServices(
            IServiceCollection serviceCollection,
            HeadlessSettingsSetupBuilder setup
        )
        {
            if (setup.Extensions.Count != 1)
            {
                throw new InvalidOperationException(
                    setup.Extensions.Count == 0
                        ? "Headless.Settings requires exactly one storage provider. Call one of `UseEntityFramework`, `UsePostgreSql`, or `UseSqlServer`."
                        : "Headless.Settings requires exactly one storage provider. Multiple storage providers were configured."
                );
            }

            serviceCollection.Configure<SettingsStorageOptions, SettingsStorageOptionsValidator>(options =>
            {
                options.Schema = setup.StorageOptions.Schema;
                options.SettingValuesTableName = setup.StorageOptions.SettingValuesTableName;
                options.SettingDefinitionsTableName = setup.StorageOptions.SettingDefinitionsTableName;
            });

            foreach (var extension in setup.Extensions)
            {
                extension.AddServices(serviceCollection);
            }

            return new HeadlessSettingsBuilder(serviceCollection);
        }
    }
}

[PublicAPI]
public static class SetupSettingsEntityFramework
{
    extension(HeadlessSettingsSetupBuilder setup)
    {
        public HeadlessSettingsSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkSettingsOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    private sealed class EntityFrameworkSettingsOptionsExtension(Type dbContextType) : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.TryAddSingleton(
                typeof(ISettingValueRecordRepository),
                typeof(EfSettingValueRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddSingleton(
                typeof(ISettingDefinitionRecordRepository),
                typeof(EfSettingDefinitionRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton(
                    typeof(IHostedService),
                    typeof(SettingsEntityValidationStartupGate<>).MakeGenericType(dbContextType)
                )
            );
        }
    }
}
