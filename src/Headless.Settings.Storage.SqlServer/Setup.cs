// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Storage;
using Headless.Settings;
using Headless.Settings.Repositories;
using Headless.Settings.SqlServer;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupSettingsSqlServer
{
    extension(HeadlessSettingsSetupBuilder setup)
    {
        public HeadlessSettingsSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessSettingsSetupBuilder UseSqlServer(Action<SqlServerSettingsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerSettingsOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerSettingsOptionsExtension(Action<SqlServerSettingsOptions> configure)
        : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<SqlServerSettingsOptions, SqlServerSettingsOptionsValidator>(configure);
            services.AddInitializerHostedService<SqlServerSettingsStorageInitializer>();
            services.TryAddSingleton<ISettingsStorageInitializer>(sp =>
                sp.GetRequiredService<SqlServerSettingsStorageInitializer>()
            );
            services.TryAddSingleton<ISettingValueRecordRepository, SqlServerSettingValueRecordRepository>();
            services.TryAddSingleton<ISettingDefinitionRecordRepository, SqlServerSettingDefinitionRecordRepository>();
        }
    }
}
