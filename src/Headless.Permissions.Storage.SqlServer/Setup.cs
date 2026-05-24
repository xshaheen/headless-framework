// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Storage;
using Headless.Permissions;
using Headless.Permissions.Repositories;
using Headless.Permissions.SqlServer;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupPermissionsSqlServer
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
        public HeadlessPermissionsSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessPermissionsSetupBuilder UseSqlServer(Action<SqlServerPermissionsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerPermissionsOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerPermissionsOptionsExtension(Action<SqlServerPermissionsOptions> configure)
        : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<SqlServerPermissionsOptions, SqlServerPermissionsOptionsValidator>(configure);
            services.AddInitializerHostedService<SqlServerPermissionsStorageInitializer>();
            services.TryAddSingleton<IPermissionGrantRepository, SqlServerPermissionGrantRepository>();
            services.TryAddSingleton<IPermissionDefinitionRecordRepository, SqlServerPermissionDefinitionRecordRepository>();
        }
    }
}
