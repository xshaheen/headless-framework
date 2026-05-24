// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Storage;
using Headless.Permissions;
using Headless.Permissions.Internal;
using Headless.Permissions.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupPermissions
{
    extension(IServiceCollection services)
    {
        public HeadlessPermissionsBuilder AddHeadlessPermissions(Action<HeadlessPermissionsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessPermissionsSetupBuilder(services);
            configure(setup);

            return _RegisterCorePermissionsServices(services, setup);
        }

        private static HeadlessPermissionsBuilder _RegisterCorePermissionsServices(
            IServiceCollection serviceCollection,
            HeadlessPermissionsSetupBuilder setup
        )
        {
            if (setup.Extensions.Count != 1)
            {
                throw new InvalidOperationException(
                    setup.Extensions.Count == 0
                        ? "Headless.Permissions requires exactly one storage provider. Call one of `UseEntityFramework`, `UsePostgreSql`, or `UseSqlServer`."
                        : "Headless.Permissions requires exactly one storage provider. Multiple storage providers were configured."
                );
            }

            serviceCollection.Configure<PermissionsStorageOptions, PermissionsStorageOptionsValidator>(options =>
            {
                options.Schema = setup.StorageOptions.Schema;
                options.PermissionGrantsTableName = setup.StorageOptions.PermissionGrantsTableName;
                options.PermissionDefinitionsTableName = setup.StorageOptions.PermissionDefinitionsTableName;
                options.PermissionGroupDefinitionsTableName = setup.StorageOptions.PermissionGroupDefinitionsTableName;
            });

            foreach (var extension in setup.Extensions)
            {
                extension.AddServices(serviceCollection);
            }

            return new HeadlessPermissionsBuilder(serviceCollection);
        }
    }
}

[PublicAPI]
public static class SetupPermissionsEntityFramework
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
        public HeadlessPermissionsSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkPermissionsOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    private sealed class EntityFrameworkPermissionsOptionsExtension(Type dbContextType) : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.TryAddSingleton(
                typeof(IPermissionGrantRepository),
                typeof(EfPermissionGrantRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddSingleton(
                typeof(IPermissionDefinitionRecordRepository),
                typeof(EfPermissionDefinitionRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton(
                    typeof(IHostedService),
                    typeof(PermissionsEntityValidationStartupGate<>).MakeGenericType(dbContextType)
                )
            );
        }
    }
}
