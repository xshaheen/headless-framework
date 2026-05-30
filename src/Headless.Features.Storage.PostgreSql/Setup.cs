// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
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
        : IFeaturesStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<PostgreSqlFeaturesOptions, PostgreSqlFeaturesOptionsValidator>(configure);
            services.AddOptions<FeaturesStorageOptions, PostgreSqlFeaturesStorageOptionsValidator>();
            services.AddInitializerHostedService<PostgreSqlFeaturesStorageInitializer>();
            services.TryAddSingleton<IFeatureValueRecordRepository, PostgreSqlFeatureValueRecordRepository>();
            services.TryAddSingleton<IFeatureDefinitionRecordRepository, PostgreSqlFeatureDefinitionRecordRepository>();
        }
    }

    private sealed class PostgreSqlFeaturesStorageOptionsValidator : AbstractValidator<FeaturesStorageOptions>
    {
        public PostgreSqlFeaturesStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidPgIdentifier();
            RuleFor(x => x.FeatureValuesTableName).IsValidPgIdentifier();
            RuleFor(x => x.FeatureDefinitionsTableName).IsValidPgIdentifier();
            RuleFor(x => x.FeatureGroupDefinitionsTableName).IsValidPgIdentifier();
        }
    }
}
