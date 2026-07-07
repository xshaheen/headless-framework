// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Features.PostgreSql;
using Headless.Features.Repositories;
using Headless.Serializer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Features;

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

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>Registers the PostgreSQL features storage provider, binding options from <paramref name="configuration"/>.</summary>
        /// <param name="configuration">Configuration section to bind to <see cref="PostgreSqlFeaturesOptions"/>.</param>
        /// <returns>The same <see cref="HeadlessFeaturesSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessFeaturesSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlFeaturesOptionsExtension(configuration));

            return setup;
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

        /// <summary>Registers the PostgreSQL features storage provider using a configuration delegate with access to the <see cref="IServiceProvider"/>.</summary>
        /// <param name="configure">Delegate that configures <see cref="PostgreSqlFeaturesOptions"/> with service resolution.</param>
        /// <returns>The same <see cref="HeadlessFeaturesSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessFeaturesSetupBuilder UsePostgreSql(Action<PostgreSqlFeaturesOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlFeaturesOptionsExtension(configure));

            return setup;
        }
    }

    /// <summary>Wires the PostgreSQL provider services into the DI container when applied to the features setup builder.</summary>
    private sealed class PostgreSqlFeaturesOptionsExtension : IFeaturesStorageOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlFeaturesOptions>? _configure;
        private readonly Action<PostgreSqlFeaturesOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlFeaturesOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlFeaturesOptionsExtension(Action<PostgreSqlFeaturesOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlFeaturesOptionsExtension(Action<PostgreSqlFeaturesOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlFeaturesOptions, PostgreSqlFeaturesOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlFeaturesOptions, PostgreSqlFeaturesOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<PostgreSqlFeaturesOptions, PostgreSqlFeaturesOptionsValidator>(
                    _configureWithServices
                );
            }

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
