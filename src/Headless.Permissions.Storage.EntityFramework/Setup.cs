// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Storage;
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
