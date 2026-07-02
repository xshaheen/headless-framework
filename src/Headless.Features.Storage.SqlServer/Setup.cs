// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Features;
using Headless.Features.Repositories;
using Headless.Features.SqlServer;
using Headless.Serializer;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods that register the SQL Server features storage provider.</summary>
[PublicAPI]
public static class SetupFeaturesSqlServer
{
    extension(HeadlessFeaturesSetupBuilder setup)
    {
        /// <summary>Registers the SQL Server features storage provider using <paramref name="connectionString"/>.</summary>
        /// <param name="connectionString">SQL Server connection string.</param>
        /// <returns>The same <see cref="HeadlessFeaturesSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or white space.</exception>
        public HeadlessFeaturesSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>Registers the SQL Server features storage provider using a configuration delegate.</summary>
        /// <param name="configure">Delegate that configures <see cref="SqlServerFeaturesOptions"/>.</param>
        /// <returns>The same <see cref="HeadlessFeaturesSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
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
            services.AddOptions<FeaturesStorageOptions, SqlServerFeaturesStorageOptionsValidator>();
            services.AddInitializerHostedService<SqlServerFeaturesStorageInitializer>();
            services.TryAddSingleton<IJsonSerializer>(_ => new SystemJsonSerializer());
            services.TryAddSingleton<IFeatureValueRecordRepository, SqlServerFeatureValueRecordRepository>();
            services.TryAddSingleton<IFeatureDefinitionRecordRepository, SqlServerFeatureDefinitionRecordRepository>();
        }
    }

    private sealed class SqlServerFeaturesStorageOptionsValidator : AbstractValidator<FeaturesStorageOptions>
    {
        public SqlServerFeaturesStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidSqlServerIdentifier();
            RuleFor(x => x.FeatureValuesTableName).IsValidSqlServerIdentifier();
            RuleFor(x => x.FeatureDefinitionsTableName).IsValidSqlServerIdentifier();
            RuleFor(x => x.FeatureGroupDefinitionsTableName).IsValidSqlServerIdentifier();
        }
    }
}
