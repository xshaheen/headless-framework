// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Storage;
using Headless.Permissions;
using Headless.Permissions.PostgreSql;
using Headless.Permissions.Repositories;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupPermissionsPostgreSql
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
        public HeadlessPermissionsSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessPermissionsSetupBuilder UsePostgreSql(Action<PostgreSqlPermissionsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlPermissionsOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlPermissionsOptionsExtension(Action<PostgreSqlPermissionsOptions> configure)
        : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<PostgreSqlPermissionsOptions, PostgreSqlPermissionsOptionsValidator>(configure);
            services.AddInitializerHostedService<PostgreSqlPermissionsStorageInitializer>();
            services.TryAddSingleton<IPermissionsStorageInitializer>(sp =>
                sp.GetRequiredService<PostgreSqlPermissionsStorageInitializer>()
            );
            services.TryAddSingleton<IPermissionGrantRepository, PostgreSqlPermissionGrantRepository>();
            services.TryAddSingleton<IPermissionDefinitionRecordRepository, PostgreSqlPermissionDefinitionRecordRepository>();
        }
    }
}
