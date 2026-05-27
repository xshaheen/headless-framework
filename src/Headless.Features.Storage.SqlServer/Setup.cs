// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features;
using Headless.Features.Repositories;
using Headless.Features.SqlServer;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupFeaturesSqlServer
{
    extension(HeadlessFeaturesSetupBuilder setup)
    {
        public HeadlessFeaturesSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessFeaturesSetupBuilder UseSqlServer(Action<SqlServerFeaturesOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerFeaturesOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerFeaturesOptionsExtension(Action<SqlServerFeaturesOptions> configure)
        : IFeaturesStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<SqlServerFeaturesOptions, SqlServerFeaturesOptionsValidator>(configure);
            services.AddInitializerHostedService<SqlServerFeaturesStorageInitializer>();
            services.TryAddSingleton<IFeatureValueRecordRepository, SqlServerFeatureValueRecordRepository>();
            services.TryAddSingleton<IFeatureDefinitionRecordRepository, SqlServerFeatureDefinitionRecordRepository>();
        }
    }
}
