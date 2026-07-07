// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Features;
using Headless.Features.Repositories;
using Headless.Serializer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Features.SqlServer;

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

        /// <summary>Registers the SQL Server features storage provider, binding options from <paramref name="configuration"/>.</summary>
        /// <param name="configuration">Configuration section to bind to <see cref="SqlServerFeaturesOptions"/>.</param>
        /// <returns>The same <see cref="HeadlessFeaturesSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessFeaturesSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerFeaturesOptionsExtension(configuration));

            return setup;
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

        /// <summary>Registers the SQL Server features storage provider using a configuration delegate with access to the <see cref="IServiceProvider"/>.</summary>
        /// <param name="configure">Delegate that configures <see cref="SqlServerFeaturesOptions"/> with service resolution.</param>
        /// <returns>The same <see cref="HeadlessFeaturesSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessFeaturesSetupBuilder UseSqlServer(Action<SqlServerFeaturesOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerFeaturesOptionsExtension(configure));

            return setup;
        }
    }

    /// <summary>Wires the SQL Server provider services into the DI container when applied to the features setup builder.</summary>
    private sealed class SqlServerFeaturesOptionsExtension : IFeaturesStorageOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqlServerFeaturesOptions>? _configure;
        private readonly Action<SqlServerFeaturesOptions, IServiceProvider>? _configureWithServices;

        public SqlServerFeaturesOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqlServerFeaturesOptionsExtension(Action<SqlServerFeaturesOptions> configure)
        {
            _configure = configure;
        }

        public SqlServerFeaturesOptionsExtension(Action<SqlServerFeaturesOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqlServerFeaturesOptions, SqlServerFeaturesOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<SqlServerFeaturesOptions, SqlServerFeaturesOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<SqlServerFeaturesOptions, SqlServerFeaturesOptionsValidator>(_configureWithServices);
            }

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
