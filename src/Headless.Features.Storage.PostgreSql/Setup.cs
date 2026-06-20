// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Features;
using Headless.Features.PostgreSql;
using Headless.Features.Repositories;
using Headless.Serializer;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods that register the PostgreSQL features storage provider.</summary>
[PublicAPI]
public static class SetupFeaturesPostgreSql
{
    extension(HeadlessFeaturesSetupBuilder setup)
    {
        /// <summary>Registers the PostgreSQL features storage provider using <paramref name="connectionString"/>.</summary>
        /// <param name="connectionString">PostgreSQL connection string.</param>
        /// <returns>The same <see cref="HeadlessFeaturesSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or white space.</exception>
        public HeadlessFeaturesSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        /// <summary>Registers the PostgreSQL features storage provider using a configuration delegate.</summary>
        /// <param name="configure">Delegate that configures <see cref="PostgreSqlFeaturesOptions"/>.</param>
        /// <returns>The same <see cref="HeadlessFeaturesSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
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
            services.TryAddSingleton<IJsonSerializer>(_ => new SystemJsonSerializer());
            services.TryAddSingleton<IFeatureValueRecordRepository, PostgreSqlFeatureValueRecordRepository>();
            services.TryAddSingleton<IFeatureDefinitionRecordRepository, PostgreSqlFeatureDefinitionRecordRepository>();
        }
    }

    private sealed class PostgreSqlFeaturesStorageOptionsValidator : AbstractValidator<FeaturesStorageOptions>
    {
        public PostgreSqlFeaturesStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidPostgreSqlIdentifier();
            RuleFor(x => x.FeatureValuesTableName).IsValidPostgreSqlIdentifier();
            RuleFor(x => x.FeatureDefinitionsTableName).IsValidPostgreSqlIdentifier();
            RuleFor(x => x.FeatureGroupDefinitionsTableName).IsValidPostgreSqlIdentifier();
        }
    }
}
