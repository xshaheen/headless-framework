// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Storage;
using Headless.Features;
using Headless.Features.PostgreSql;
using Headless.Features.Repositories;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupFeaturesPostgreSql
{
    extension(HeadlessFeaturesSetupBuilder setup)
    {
        public HeadlessFeaturesSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessFeaturesSetupBuilder UsePostgreSql(Action<PostgreSqlFeaturesOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlFeaturesOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlFeaturesOptionsExtension(Action<PostgreSqlFeaturesOptions> configure)
        : IStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<PostgreSqlFeaturesOptions, PostgreSqlFeaturesOptionsValidator>(configure);
            services.AddInitializerHostedService<PostgreSqlFeaturesStorageInitializer>();
            services.TryAddSingleton<IFeatureValueRecordRepository, PostgreSqlFeatureValueRecordRepository>();
            services.TryAddSingleton<IFeatureDefinitionRecordRepository, PostgreSqlFeatureDefinitionRecordRepository>();
        }
    }
}
