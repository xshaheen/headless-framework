// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Storage;
using Headless.Settings;
using Headless.Settings.PostgreSql;
using Headless.Settings.Repositories;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupSettingsPostgreSql
{
    extension(HeadlessSettingsSetupBuilder setup)
    {
        public HeadlessSettingsSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessSettingsSetupBuilder UsePostgreSql(Action<PostgreSqlSettingsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlSettingsOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlSettingsOptionsExtension(Action<PostgreSqlSettingsOptions> configure)
        : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<PostgreSqlSettingsOptions, PostgreSqlSettingsOptionsValidator>(configure);
            services.AddInitializerHostedService<PostgreSqlSettingsStorageInitializer>();
            services.TryAddSingleton<ISettingValueRecordRepository, PostgreSqlSettingValueRecordRepository>();
            services.TryAddSingleton<ISettingDefinitionRecordRepository, PostgreSqlSettingDefinitionRecordRepository>();
        }
    }
}
