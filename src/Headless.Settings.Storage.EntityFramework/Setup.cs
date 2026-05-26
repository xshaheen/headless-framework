// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Storage;
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
